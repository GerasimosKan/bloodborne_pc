// SPDX-FileCopyrightText: Copyright 2026 bbport contributors
// SPDX-License-Identifier: GPL-2.0-or-later
// Self-update from GitHub releases: tag v<version>, assets UPDATE.<version>-Bloodborne-PC.zip and
// SHA256SUMS (packaging/windows/repack.ps1). The zip holds program files only; bbport.ini, user\,
// mods\, saves and the launcher settings (%APPDATA%) are not in it, so they survive an update.
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace BbportLauncher;

public partial class MainWindow
{
    private const string UpdateRepo = "GerasimosKan/bloodborne_pc";
    private string? updateTag, updateZipUrl, updateSumsUrl;

    private static string InstalledVersion()
    {
        var file = Path.Combine(Store.PackageDir, "VERSION");
        return File.Exists(file) ? File.ReadAllText(file).Trim() : "";
    }

    // Only a package (Bloodborne.exe in the package folder) updates itself, never a dev build.
    private static bool IsPackage => Environment.ProcessPath is string exe &&
        Path.GetFileName(exe).Equals("Bloodborne.exe", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetDirectoryName(exe), Store.PackageDir, StringComparison.OrdinalIgnoreCase) &&
        File.Exists(Path.Combine(Store.PackageDir, "bbport-run.exe"));

    private static HttpClient UpdateHttp(int seconds)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Bloodborne-launcher"); // GitHub's API rejects requests without one
        return http;
    }

    // SHA256SUMS lines are "<hex>  <name>" (sha256sum format, optional '*' for binary mode).
    internal static string? ExpectedSha256(string sums, string fileName) =>
        sums.Split('\n').Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == fileName)?[0];

    // "v0.7.10" -> [0, 7, 10]; null when it is not a plain version number.
    internal static int[]? VersionParts(string tag)
    {
        var parts = tag.Trim().TrimStart('v', 'V').Split('.');
        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return null;
        return numbers;
    }

    // Numeric, so 0.7.10 > 0.7.9; never an older or the same release. An unknown install takes any valid release.
    internal static bool IsNewerRelease(string latest, string installed)
    {
        if (VersionParts(latest) is not int[] next) return false;
        if (VersionParts(installed) is not int[] current) return true;
        for (int i = 0; i < Math.Max(next.Length, current.Length); i++)
        {
            int a = i < next.Length ? next[i] : 0, b = i < current.Length ? current[i] : 0;
            if (a != b) return a > b;
        }
        return false;
    }

    // Extracts over root, dropping the zip's single top folder when every entry has it.
    internal static void ExtractPackage(ZipArchive archive, string root)
    {
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var tops = archive.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Distinct().ToList();
        string? top = tops.Count == 1 && tops[0] is not ("" or "." or "..") ? tops[0] : null;
        bool strip = top != null && archive.Entries.All(e => e.FullName.Replace('\\', '/').Contains('/'));
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('\\', '/');
            if (strip) name = name[(top!.Length + 1)..];
            if (name == "") continue;
            string target = Path.GetFullPath(Path.Combine(rootFull, name));
            if (!target.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The package contains a path outside the install folder: " + entry.FullName);
            if (name.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    private async void CheckForUpdate()
    {
        if (!IsPackage) return;
        try { File.Delete(Environment.ProcessPath + ".old"); } catch { }
        try
        {
            using var http = UpdateHttp(10);
            using var release = JsonDocument.Parse(await http.GetStringAsync($"https://api.github.com/repos/{UpdateRepo}/releases/latest"));
            string tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
            if (!IsNewerRelease(tag, InstalledVersion())) return;
            foreach (var asset in release.RootElement.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (name.StartsWith("UPDATE.", StringComparison.OrdinalIgnoreCase) && name.EndsWith("-Bloodborne-PC.zip", StringComparison.OrdinalIgnoreCase)) updateZipUrl = url;
                else if (name.StartsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase)) updateSumsUrl = url;
            }
            if (updateZipUrl == null || updateSumsUrl == null) return; // never install an unverifiable package
            updateTag = tag;
            UpdateButton.Content = string.Format(T("update_available"), tag);
            UpdateButton.Visibility = Visibility.Visible;
        }
        catch { } // offline or rate-limited: the launcher works without the check
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        if (updateTag == null || updateZipUrl == null || updateSumsUrl == null || Environment.ProcessPath is not string exe) return;
        if (game is not null) { MessageBox.Show(this, T("update_close_game"), T("update_title")); return; }
        if (MessageBox.Show(this, string.Format(T("update_confirm"), updateTag, UpdateRepo), T("update_title"),
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;

        string label = UpdateButton.Content as string ?? "";
        string zip = Path.Combine(Path.GetTempPath(), $"Bloodborne-update-{Guid.NewGuid():N}.zip");
        string old = exe + ".old";
        bool moved = false;
        var restart = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Store.PackageDir }; // loaded before the exe is renamed
        UpdateButton.IsEnabled = false;
        try
        {
            using var http = UpdateHttp(1800);
            string zipName = Path.GetFileName(new Uri(updateZipUrl).LocalPath);
            string expected = ExpectedSha256(await http.GetStringAsync(updateSumsUrl), zipName)
                ?? throw new IOException("The release lists no checksum for " + zipName + ".");

            using (var response = await http.GetAsync(updateZipUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? 0, done = 0;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(zip);
                var buffer = new byte[1 << 20];
                for (int read; (read = await source.ReadAsync(buffer)) > 0;)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    done += read;
                    UpdateButton.Content = total > 0 ? $"{done * 100 / total}%" : $"{done >> 20} MB";
                }
            }

            string actual;
            await using (var file = File.OpenRead(zip)) actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Checksum mismatch: the download is corrupt.");

            // Open the archive before renaming: a single-file launcher loads ZipFile from its own exe on first use.
            using (var archive = ZipFile.OpenRead(zip))
            {
                File.Move(exe, old, true); // Windows lets a running exe be renamed, not overwritten
                moved = true;
                await Task.Run(() => ExtractPackage(archive, Store.PackageDir));
            }
        }
        catch (Exception ex)
        {
            // Extraction is not atomic: put the old launcher back; VERSION is only replaced by a full
            // extraction, so the button offers a full retry.
            if (moved) try { File.Move(old, exe, true); } catch { }
            UpdateButton.Content = label;
            UpdateButton.IsEnabled = true;
            MessageBox.Show(this, string.Format(T("update_failed"), ex.Message), T("update_title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally { try { File.Delete(zip); } catch { } }

        try { Process.Start(restart); } catch { }
        Application.Current.Shutdown();
    }
}
