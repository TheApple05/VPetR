# Rimuru desktop companion

An English Rimuru character and animation upgrade for [TheApple05/VPetR](https://github.com/TheApple05/VPetR). The generated artwork includes 25 pose families, 153 source frames, 360 exported PNG frames, and 150 native animation definitions.

New loops cover sleeping, head and body pats, eating, drinking, talking, sitting, stretching, celebrating, studying, carrying, and resting. Movement, waving, jumping, focused work, waiting, and inspection remain available. English is the default language and Rimuru is the default character.

![New animation previews](Library/Pets/Rimuru/native-expansion/previews/new-animations.gif)

## Idle behavior and transitions

Four new idle loops add looking around, scarf adjustment, yawning, and contented swaying. Low energy favors rest; late hours favor quiet actions; cheerful daytime allows swaying; longer inactivity prompts stretches, sitting, inspection, and occasional walking when there is room. Hunger and thirst lead to waiting animations without granting free food or drink. This works with simulation disabled too, using time and interaction cues.

The policy leaves work, sleep, dragging, and recent clicks alone, uses a 35-second cooldown (rounded to the normal 15-second event tick), and excludes the previous idle choice. It replaces random sleep interruptions for this character. Start, loop, and end use separate sequences with matching boundary poses. A 160 ms crossfade softens changes between poses and safely handles interrupted transitions. Same-loop restarts skip the blend. Other characters retain their original scheduling and can opt into these features through their character config.

## Build and run

Use Windows x64, the .NET 10 SDK, and Python 3.10 or newer. From the repository root:

```powershell
python -m pip install -r .\Rimuru\requirements.txt
powershell -ExecutionPolicy Bypass -File .\Rimuru\Build-Rimuru.ps1
.\Rimuru\Launch-Rimuru.cmd
```

The self-contained build is written to `Rimuru/dist/Rimuru-VPet`. The builder exports the checked-in artwork and includes the required Core assets. It preserves existing settings in the selected output folder. `-UseStandardAtlas` uses the nine-state atlas without the additional directional look rows; native desktop animations stay available.

To use just the character, copy `VPet-Simulator.Windows/mod/0100_rimuru` into another VPet installation's `mod` folder, enable **Rimuru Companion**, select **Rimuru**, choose **English**, and restart. That installation still needs its normal Core module.

## Automatic updates

On normal launch and every six hours while running, the app checks stable `rimuru-v` releases from **TheApple05/VPetR**. The helper downloads, verifies GitHub's SHA-256 digest, and fully validates/extracts the package before requesting shutdown. The app saves every open profile and confirms that its fresh snapshot and settings exist before closing. The helper waits for process exit, installs the update, and reopens the app with its previous launch arguments. It never force-kills the pet. This works without a visible main-window handle.

Settings, saves, and other mods are preserved. Failed saves cancel the restart. Copy failures trigger rollback and reopen the previous app; failed versions have a six-hour retry delay to prevent restart loops. Replaced program files are backed up in the Windows temporary folder. Offline checks leave the installed version usable. Diagnostic launches skip network checks but still support the save/shutdown protocol for isolated testing.

Create an empty `disable-auto-updates.txt` beside the executable to turn off checks. Update results are written to `rimuru-update.log`. Older versions need a one-time upgrade to 2.3.0 to gain automatic saving and reopening. IPC request/reply and retry records stay local and are excluded from release packages.

## Native checks

`powershell -NoProfile -ExecutionPolicy Bypass -File .\Rimuru\tests\Auto-Restart-Checks.ps1` exercises the actual app in the isolated build folder: invalid requests, failed-save cancellation, persisted snapshot/settings, graceful exit, and a single restart. Run it only against the disposable build output.

Run `python .\Rimuru\tests\check_phase_assets.py` for phase continuity and `dotnet run --project .\Rimuru\tests\RimuruChecks -c Release -p:CETCompat=false -- --behavior` for idle policy checks. The mod check also tests real WPF fades, interrupted fades, and live work/sleep/drag/click guards.

The WPF check program tests frozen-frame reuse, playback restarts, idle eviction, source-change invalidation, truncated-cache recovery, and disposal. Its mod check resolves 284 mood/action combinations and plays all 25 pose families through the real native renderer.

```powershell
New-Item -ItemType Directory -Force .\Rimuru\dist\checks | Out-Null
dotnet run --project .\Rimuru\tests\RimuruChecks -c Release -p:CETCompat=false -- .\VPet-Simulator.Windows\mod\0100_rimuru\pet\rimuru\frames\idle .\Rimuru\dist\checks\cache.json
dotnet run --project .\Rimuru\tests\RimuruChecks -c Release -p:CETCompat=false -- --mod .\VPet-Simulator.Windows\mod\0100_rimuru .\Rimuru\dist\checks\native.json
```

The 2.1 animation/cache baseline passed local Windows checks. In one controlled 30-second idle comparison after warmup, CPU usage fell from 1.85% to 0.60% of one core and mean private memory from 357.5 to 128.8 MiB. The focused 20,000-frame lookup check fell from 216.1 to 16.3 ms and from 11.84 MB allocated to 328 bytes. These are local measurements, not guarantees; startup did not improve.

`scripts/Measure-Rimuru.ps1` measures an isolated build. Run it only against a disposable profile: diagnostics write files beside the executable. Keep movement and simulation settings identical between comparisons. `scripts/package_delivery.py` creates portable and character ZIPs from the default build output, excludes saves and caches, and checks the archive's integrity.

## Artwork and scope

The source atlas, generated strips, extracted frames, pose hashes, and previews are included. Original private reference photos and account-specific ChatGPT Pets records are excluded. The optional strip extraction and base-atlas preview helpers require the installed Pets plugin; building from the checked-in frames does not.

Eating and drinking use one bread roll and mug across their corresponding items. Wall climbing and full Steam integration are outside the validated scope. The ChatGPT Pet sprite format remains the original nine animation states and sixteen look directions; the extra sixteen loops are native desktop animations.

This fork retains VPet's existing [license](../LICENSE) and attribution. See the [upstream English guide](../README_en.md) for the original project's documentation. Development and contributions for this version go only to TheApple05/VPetR.
