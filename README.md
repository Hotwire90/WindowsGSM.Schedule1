<p align="center">
  <img src="s1.jpg" alt="WindowsGSM.Schedule1" width="400">
</p>
# WindowsGSM.Schedule1

A WindowsGSM plugin for hosting **Schedule I** dedicated servers, powered by the community mod [DedicatedServerMod (S1DS)](https://github.com/ifBars/S1DedicatedServers) by ifBars.

Schedule I has no official dedicated server. This plugin automates installing the base game via SteamCMD, then deploying [MelonLoader](https://github.com/LavaGang/MelonLoader) (the mod-loading framework) and DedicatedServerMod on top of it, so the game runs as a real headless server instead of a normal client.

---

## Installation

1. Drop the `Schedule1.cs` folder into WindowsGSM's `plugins` folder.
2. In WindowsGSM, click **Reload Plugins**.
3. **Add Server** > select **Schedule I Dedicated Server (S1DS)**.
4. When prompted, log in with a Steam account that **owns Schedule I**. Anonymous SteamCMD login will not work - this is a paid Early Access title with no free/anonymous server depot, unlike games such as Valheim that ship a separate free dedicated-server App ID.
5. Let Install run. On first install, the plugin automatically:
   - Downloads and extracts MelonLoader (Windows x64 build) into the game folder.
   - Downloads and extracts the matching DedicatedServerMod package (auto-detects IL2CPP vs Mono by checking for `GameAssembly.dll`).
6. Click **Start**. The very first launch will take noticeably longer than normal - MelonLoader has to generate IL2CPP interop assemblies once. Subsequent launches are fast.

**IL2CPP is the correct target for current Schedule I releases.** The plugin auto-detects this for you and will deploy the IL2CPP build of DedicatedServerMod - you shouldn't need to do anything about it, but if you ever see it behaving like it deployed the Mono build instead, something's wrong (e.g. `GameAssembly.dll` missing from the game folder for some reason). IL2CPP is what every current install should end up using.

### Ports

- **Server Port** (default `38465`) - UDP for gameplay, also used by S1DS's own TCP status-query endpoint.
- **Query Port** (default `27016`) - Steam game server query port (`steamGameServerQueryPort`), only relevant if using `SteamGameServer` auth.

Forward both if you want players connecting from outside your LAN. See "Connecting from a client" below for why a Steam server browser / invite won't work here.

---

## Post-install configuration

After the first successful Start/Stop, two files get generated under `serverfiles\UserData\`:

- **`server_config.toml`** - the actual server config (name, ports, auth, gameplay, autosave, etc). This is the main file you'll want to review.
- **`permissions.toml`** - role hierarchy (`default` → `support` → `moderator` → `administrator` → `operator`) for players/admins using in-game or chat commands. Doesn't affect the local host console.

You don't need to touch `MelonPreferences.cfg`. Running through WindowsGSM already suppresses everything about MelonLoader's own console - it never pops up a window at all - so there's nothing to configure there.

Everything about how the server behaves (auth, mod verification, gameplay, etc.) is controlled through `server_config.toml`. You don't need to worry about command-line arguments or WindowsGSM's Additional Parameters field for any of this - it's all handled through the config file.

**A few settings are controlled by WindowsGSM itself, not the config file:** Server Name, Server Port, Server Query Port, Server Maxplayer, and Server GSLT in WindowsGSM's own Edit Server dialog get synced into `server_config.toml` automatically every time you click Start. Edit those five from WindowsGSM, not by hand-editing the config file - any manual edit to those specific lines in `server_config.toml` will get overwritten on the next Start.

---

## Steam authentication - read this before going further

This is the single most confusing part of running this server, so here's everything we figured out.

`server_config.toml` → `[authentication]` has an `authProvider` setting with three options:

| Value | Behavior |
|---|---|
| `None` | No Steam auth at all. Players connect freely by IP. Good for local testing, not real deployments (no ownership/VAC verification). |
| `SteamGameServer` | Recommended for real hosting - the server registers itself with Steam as a game server to verify connecting players. |
| (GSLT-based) | Set `steamGameServerLogOnAnonymous = false` + a real `steamGameServerToken` instead of anonymous login. |

**Why `SteamGameServer` mode can fail to initialize:** Schedule I doesn't ship as a true standalone dedicated-server binary - it's the actual client executable being repurposed, so anonymous game-server login doesn't always work reliably.

**The fix:** use a **Game Server Login Token (GSLT)** instead of anonymous login. A GSLT is a token scoped specifically to a game-server identity - this is what Valve built for exactly this situation, and it's the recommended, reliable way to authenticate.

1. Go to `https://steamcommunity.com/dev/managegameservers` logged in as the account that owns the game.
2. Enter App ID `3164500`, generate a token.
3. Paste the token into WindowsGSM's **Server GSLT** field in the Edit Server dialog (not `server_config.toml` directly - see the note above).
4. Start the server - no Steam client needed running in the background.

Leaving Server GSLT blank keeps the server on anonymous login.

---

## Connecting from a client

This does **not** use Steam's normal P2P lobby/invite system - S1DS replaces it with direct IP:port connections. Players need MelonLoader and a matching S1DS client package:

1. A legitimate copy of Schedule I on Steam.
2. **Download MelonLoader and install it in your own game folder** - grab `MelonLoader.x64.zip` from the [MelonLoader releases page](https://github.com/LavaGang/MelonLoader/releases) and extract its contents directly into your Schedule I game folder (the one with `Schedule I.exe` in it). To find that folder: in Steam, right-click **Schedule I** > **Properties** > **Installed Files** > **Browse**.
3. Download the matching **S1DS client package** (`Il2cpp_Client.zip` or `Mono-Client.zip` - must match your server's build type; `Il2cpp` is the right one for current Schedule I releases) from the [S1DedicatedServers releases page](https://github.com/ifBars/S1DedicatedServers/releases) and extract its contents directly into the same game folder as above (the one with `Schedule I.exe` in it).
4. Launch the game normally through Steam.
5. Use the client mod's direct-connect option with your server's public IP and port (`38465` by default).

If testing on the same machine/LAN as the server, launch the client **before** starting the dedicated server - Steam can block starting a client if the game-server process for the same account/app is already running; the reverse order works fine.

---

## Support

If this plugin saved you some time, [Buy Me a Coffee](https://buymeacoffee.com/hotwire90).

---

## Credits

- [WindowsGSM](https://github.com/WindowsGSM/WindowsGSM) - the server management tool this plugin is built for
- [DedicatedServerMod (S1DS)](https://github.com/ifBars/S1DedicatedServers) by ifBars
- [MelonLoader](https://github.com/LavaGang/MelonLoader) by LavaGang
- Plugin author: Hotwire90

---

MIT License - Copyright (c) 2026 Hotwire90. See [LICENSE](LICENSE) for full terms.
