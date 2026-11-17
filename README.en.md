[![Русский](https://img.shields.io/badge/%D0%A0%D1%83%D1%81%D1%81%D0%BA%D0%B8%D0%B9-555555?style=flat-square)](README.md) | [![English — selected](https://img.shields.io/badge/English-selected-2ea44f?style=flat-square)](README.en.md) | [![한국어](https://img.shields.io/badge/%ED%95%9C%EA%B5%AD%EC%96%B4-555555?style=flat-square)](README.ko.md)

# Mimesis Escape QTE

Break free from grabs by Mimesis, Mama Monkey, and Quokka Glider by pressing a key several times.

An unofficial mod for **MIMESIS on Windows**. When the monster grabs you, a prompt shows your progress and the time remaining. Complete the QTE in time to escape.

> **Version 0.2.4 for MIMESIS 0.4.2, Steam build 26558062.** On any other game build, the mod disables itself until a compatible update is released.

## Download

**[Download the ready-to-use mod →](https://github.com/geologos07/MimesisEscape/releases)**

In Releases, choose the full ZIP package for Windows. Everything you need is included — no separate libraries or development tools are required.

## What the mod does

- Lets you attempt to escape grabs by Mimesis, Mama Monkey, and Quokka Glider.
- Shows the key, press count, and time remaining.
- Lets you choose a key in the game's settings.
- Counts separate key presses: holding the key is not enough.
- If you fail, the grab continues as usual, with no additional penalty.


## Installation

1. Close the game completely.
2. Download the ready-to-use ZIP from **Releases**.
3. In Steam, open **MIMESIS → Manage → Browse local files**.
4. Extract the ZIP contents into the folder containing `MIMESIS.exe`.
5. Launch the game through Steam as usual.

The `version.dll` file must be next to `MIMESIS.exe`, and `MimesisEscape.dll` must be in the `Mods` folder. You do not need to run any files from the archive manually.

Install using the ready-to-use package from Releases. GitHub's automatically generated **Source code** archives contain the source files.

## How to use it

By default, you need to **press and release F6 five times within a maximum of three seconds**.

Change the key here:

**Settings → Controls → Escape (QTE)**

The mod automatically uses the selected game language. All 18 game languages are supported.

Click the keyboard field and choose an unused key. The setting is saved and takes effect immediately. If the key is already assigned to another action, choose a different one.

Key presses do not count while a menu or chat is open, but the timer keeps running. Switching to another window cancels the attempt. Network latency may leave you with less time.

## Playing with friends

**The host and every player who wants to use QTE need the same version of the mod.** The host is the person who created the room.

Players without the mod can join the session, but they will not get QTE. If only the client has the mod and the host does not, QTE escape is unavailable.

## Checks

Version 0.2.4 was launched on MIMESIS 0.4.2, build 26558062. A player confirmed successful escapes from Mama Monkey and Quokka Glider; escape from Mimesis had previously been confirmed. The mod passed 79 automated checks.

The mod does not restore lost health, remove accumulated infection, or extend the grab duration.

## Uninstalling

Close the game and delete this file:

`Mods/MimesisEscape.dll`

Your saved key setting will remain in case you reinstall the mod.

## Found a problem?

[Open an issue](https://github.com/geologos07/MimesisEscape/issues) and include:

- the game and mod versions;
- whether you are the host or a client;
- what happened and what you expected;
- a screenshot of the problem, if possible.

Version history: [CHANGELOG.md](CHANGELOG.md).
