Bloodborne PC for Windows 10/11

You need your own dump of Bloodborne (CUSA03173, version 1.09) and a GPU with Vulkan 1.3 and a
current driver (AMD Adrenalin, NVIDIA GeForce, Intel Arc).

1. Unpack the zip anywhere (not into Program Files).
2. Put your CUSA03173 folder (the one with eboot.bin and sce_sys) in this folder, next to
   BloodborneLauncher.exe.
3. Start BloodborneLauncher.exe and press Play.
4. In the game: Insert (or L3+R3) opens the port's settings menu.

Updates: the launcher shows a button when a new version is out and installs it in place.
Your saves (user\), settings (bbport.ini), mods (mods\) and the game folder are kept.

Trophies earned with the older fork: copy its user\trophies.txt into user\ here; they are
imported the next time you press Play.

Everything the port writes stays in this folder: bbport.ini, user\ (saves), out\ (prepared
game files), logs\ (one log per run). For a bug report send the newest file from logs\.

Online: the launcher's Online section (Seamless Hunt or The Hunter's Dream).
Mods: the launcher's Mods section; shadPS4 XML patches for 1.09 go in patches\.
FSR 4.1.1: choose it in the launcher, then "Build from an AMD DLL..." and pick
amd_fidelityfx_upscaler_dx12.dll 4.1.x; the set is built into fsr4_411\.
DLSS (GeForce RTX): choose DLSS in the launcher or the in-game menu; Frame Generation needs
RTX 40/50.
