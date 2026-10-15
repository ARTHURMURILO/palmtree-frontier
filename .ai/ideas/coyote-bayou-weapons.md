# Idea: Coyote Bayou (BYOND) weapon port backlog

| | |
|---|---|
| **Status** | Partially implemented (2026-10) — 29 ballistic guns + 11 calibers are live in `Resources/Prototypes/_CS/Entities/Objects/Weapons/Guns/Fallout/`; the energy set remains a backlog |
| **Scope** | Port backlog of Fallout-themed guns from the `coyote-bayou` BYOND checkout into this repo (proposed `_CS` prefix, see §5) |
| **Touched areas** | `Resources/Prototypes/_CS/Entities/Objects/Weapons/Guns/Fallout/*`, `Resources/Textures/_CS/Objects/Weapons/Guns/*`, `Resources/Audio/_CS/Weapons/Fallout/*`, `.ai/PORTING.md`, `.ai/file_paths.md` |
| **Source** | `BYOND/coyote-bayou` — Fallout-themed SS13 codebase ("Fox": Citadel -> Desert Rose -> Fortuna -> The Wasteland -> Sunset Wasteland -> Fox). Listed in `.ai/file_paths.md` |
| **Research anchors** | `code/modules/projectiles/guns/**`, `modular_coyote/code/modules/znuguns/**`, `modular_sand/**`, `modular_citadel/**` |

This file parks the full scan of the coyote-bayou gun catalogue: **480 guns we do not have** (after removing templates, debug/test/toy/magic entries and the overlap we already cover), grouped by category. The maintainer judged the list too large to port in one go; §5 sketches the port plan and the decisions needed before any implementation starts.

---

## 1. Summary

- The raw scan found ~630 named gun types; after filtering and removing our existing coverage, **480 distinct guns remain** as the backlog.
- We already cover only a small overlap (see §2), so almost the whole Fallout arsenal is missing.
- The `modular_coyote` **znuguns** pack (`modular_coyote/code/modules/znuguns/code/guns/tier0-3.dm`) is a **balance overlay** over the same guns (`damage_multiplier = TIER0..3`), not a separate set — port each gun once with SS14-appropriate stats, not the tier multipliers.
- Sprites and sounds are available in the source: see §4.

## 2. What we already cover (excluded from the lists below)

energy gun, disabler, taser gun, tesla gun, pulse rifle, pulse carbine, pulse pistol, temperature gun, x-ray laser gun, antique laser gun, laser carbine, proto-kinetic accelerator, syringe gun, grenade launcher, rocket launcher, WT-550 PDW, WT-550 Sporting Carbine, Mosin-Nagant m38, Hristov, AK-47, flintlock pistol, musket, laser tag gun, practice laser gun, classic pump-action shotgun, Luger P08, CX Model D Energy Gun.

## 3. The backlog by category

### Energy — laser / energy (108)

Source: `code/modules/projectiles/guns/energy/*` (laser, laser_crank, energy_gun, pulse, stun, tesla, special, megabuster, kinetic_accelerator, dueling, mounted), `guns/misc/beam_rifle.dm`

accelerator laser cannon, advanced energy gun, advanced plasma cutter, AEP5-CR Disabling Beam Pistol,
AEP7 laser pistol, AER12 laser rifle, AER14 laser rifle, AER9 laser rifle, alien blaster, Ashen accelerator,
assaultron AEP7 laser eye, autoshock tesla pistol, Badland's Special, biological demolecularisor,
bloody accelerator, bluespace wormhole projector, Buster Cannon, candy corn crossbow, compact RCW,
Custom accelerator, Disabler RCW, DRAGnet, dueling pistol, Emitter Carbine, energy crossbow,
Energy Snare Launcher, experimental laser pistol, floral somatoray, Freeblade Blaster, Gamma gun,
Heavy accelerator, heavy laser rifle, Hot-wired AER10 laser rifle, Hot-wired AER9 laser rifle, hybrid taser,
hybrid turret gun, improvised laser, instakill rifle, integrated AER9, integrated laser pistol, ion carbine,
Kalashnikov 470, Kalashnikov 740, LAER, Laser AK470M, laser gatling gun, laser RCW, laser rifle, Laserbuss,
Latos Systems S-5 energy rifle, Lucy, M1911-P, Mega-buster, meteor gun, meteor pen, Mind Flayer,
miniature energy gun, miniture laser pistol, Modular accelerator, nuclear laser pistol, nuclear laser rifle,
one-point bluespace-gravitational manipulator, OURP-HYBRID, particle acceleration rifle, particle cannon,
pickle ray, plasma cutter, Precise accelerator, premium accelerator, Proto-buster, prototype nuclear rifle,
pulse destroyer, Rapid accelerator, Recharger Pistol, repeating blaster, Revolver Man's lazor, S.I.D.A.,
scatter laser gun, security temperature gun, shock autoblaster, sling, sling staff, small energy crossbow,
smoocher, smuggled experimental laser pistol, Solar Scorcher, tactical energy gun, tactical laser rifle,
Tesla Cannon, tribeam stunrifle, twin-shot RCW carbine, Type-2a phaser pistol, Type-2b phaser pistol,
Type-51 Carbine, Ultracite laser pistol, Ultracite laser rifle, Wattz 1000 laser pistol,
Wattz 1000 smart-laser pistol, Wattz 1000s laser pistol, wattz 2000, wattz 2000e, wattz 2000s,
weathered strange laser rifle, worn AEP7 laser pistol, Worn Badland's Special, worn compact rcw,
X-01 MultiPhase Energy Gun, YK-42b Pulse Rifle

### Energy — plasma (20)

Source: `code/modules/projectiles/guns/energy/plasmaf13.dm`, `plasma_cit.dm`

adam, ergonomic plasmacaster, eve, glock 86, glock 86a, Honorbounded Drekavac pistol,
Latos Systems EG-2A7 pistol, Latos Systems PR-M1A2 Rifle, lightweight plasma pistol, Malediction,
multiplas rifle, Neptune-35 matter modulator, Oni's Bane rifle, plasma cannon, plasma carbine, Plasma Caster,
Plasma MP40k, plasma pistol, plasma rifle, shoddy plasma pistol

### Pistols (48)

Source: `code/modules/projectiles/guns/ballistic/pistol.dm`

.22 pistol, 14mm pistol, 4.7mm A39 Pistol, 9mm autopistol, 9mm Borchardt, 9mm carbine, 9mm Luger,
9mm Makarov pistol, Aureum Tactum, Automag, Beretta M93R, Beretta M9FS, Black Kite, Browning Hi-power,
Colt N104 Defender, Colt N104-D 'Warden Ten', Colt N99 pistol, compact 14mm pistol, Crusader pistol,
Custom engraved derringer, Custom P-36 Assault Pistol, Custom Zünder-14 Pistol, Desert Eagle, El Capitan,
Glock 19, Latos Systems P-36 Assault Pistol, Little Devil 14mm pistol, M1911, M1911 Custom,
M3 Civillian Pistol, M3 Special Operations., Maria, Mini VAL, Mk. 23, mouse gun, MP77 Pistol, NP-149/40,
Painted pistol, Pink glock Pistol, RuBee, Ruby, Santa Muerte, Schmeisser Classic, Skorpion 9mm,
the Executive, Trusty Beretta M93R, Type 17, Zünder-14 Pistol

### Revolvers (38)

Source: `code/modules/projectiles/guns/ballistic/revolver.dm`

.22LR derringer, .22LR revolver, .308 Pistol, .357 magnum revolver, .44 magnum revolver,
.44 magnum single-action revolver, .45 LC derringer, .45-70 derringer, 4.7mm revolver 2190 edition.,
5mm break-action revolver, auto-revolver, bladed ranger sequoia, Colt Buntline, Colt Single Action Army,
cursed Russian revolver, deer hunting revolver, degraded hunting revolver, Deireadh le ceantar revolver,
Desert Hemlock, desert ranger revolver, desert sequoia, Engraved LeMat Revolver, Grapeshot Revolver,
Hermes Sporting Revolver, hunting revolver, Lucky, M2045 Magnum Revolver Rifle,
Medusa Multi-Caliber Revolver, MWS-01 'Big Iron', Needler pistol, OT-64 Heavy Needler rifle, Peacekeeper,
police revolver, S&W .45 ACP revolver, snubnose .44 magnum revolver, Taurus Judge, Ultracite needler,
Webley Revolver

### SMGs / automatic rifles / LMGs (127)

Source: `code/modules/projectiles/guns/ballistic/automatic.dm`

.22 machine pistol, .22 MP5, .22 Uzi, .223 arm pistol, 10mm submachine gun, 14mm SMG, 2100 G10 Rifle,
5mm Civilian G10 Rifle, 9mm Owen Gun, 9mm Rockwell Pistol, 9mm Rockwell SMG, 9mm Uzi, A11 'Matilda' Rifle,
Advanced Violet Needler Rifle, Ak74u, ALR15, American 18-bee, American 180, American Commonwealth Carbine,
AR-10 Armalite, AS-VAL Supressed rifle, assault carbine, AUG A10 rifle, AUG P-Skulljack,
battle-worn marksman carbine, beat up .45ACP submachine gun, Big Bienvenue, Blessed Trusty Combat Carbine,
Bozar, Bren gun, browning automatic rifle, Browning M1919, Carl Gustaf 10mm, Chinese 4.7mm assault rifle,
Colt Rangemaster, Combat Carbine, commando carbine, compact sniper rifle,
Custom 'Cold Whisper'Assault Carbine, Custom Bren gun, Custom Enfield SLR, Custom Engraved STG-44,
Custom FG-42 rifle, Custom G41 rifle, Custom P47 Rifle, Custom T55E1 Assault Carbine, De Lisle carbine,
DP-27, Enfield SLR, Engraved Zastava M70, Explorer sniper rifle, FN FAL, FN P90c, Fusil D'assaut F1, G11,
golden sniper rifle, HK MP-5, infiltrator, Ingram Model 10, KF-21 Black Panther Needler LMG,
Knockoff Ingram Model 10, L1A1, Latos Systems Cromwell-55 shotgun rifle, Lewis automatic rifle,
Lewis Mark II, Light Support Weapon, M1 carbine, M1 Garand, M1-22 carbine, M1A1 carbine, M2 carbine,
M22 Night Ops SMG, M36 'Justice' battle rifle, m3a1 greasegun, m41 battle rifle, M72 gauss rifle,
marksman carbine, Maschinenpistole 40, Mauser M712, Mini DP-27, Mini PPSh, multi-caliber carbine,
multi-caliber magnum, multi-caliber smg, Old Glory, Oststrauß, P47 Battle rifle, Police Assault Rifle,
Police Rifle, Ppsh-41, PSG-5 rifle, R-varlden mock bullpup, R82 heavy service rifle, R84 LMG,
r91 assault rifle, R93 PDW, Ratling Gun, Ratslayer, Republic's Pride, Russian RPK LMG,
Saiga-12 Assault shotgun, Scar-L rifle, scout carbine, service rifle, Sig Rifle 550, SKS, sniper rifle,
sport carbine, Storm Drum, T25 Assault Carbine, T55E1 Assault Rifle, The People's Rifle, Thompson SMG,
Tox's G11M, type 93 assault rifle, Unktehila, varmint rifle, verminkiller rifle, worn assault carbine,
Worn Carl Gustaf 10mm, Worn FN P90c, Worn fusil assaut G80, Worn NR-43 Turán, Worn S-27 Akula Needler SMG,
worn-out 10mm submachine gun, xl70e3, Z34 Battle Rifle

### Rifles (33)

Source: `code/modules/projectiles/guns/ballistic/rifle.dm`

.22 Mares Leg, anti-material rifle, Boys anti-tank rifle, brush gun, cowboy repeater, coyote repeater,
Custom TankGewehr M1918, Fusil Energie, Gras, Hephestus Ferromagnetic Rifle, hunting rifle,
hypocritical oath, Jungle Carbine, Lee Einfield 'Speed' rifle., Lee-Enfield rifle, Lever action pistol,
long ranger repeater, Mauser Model 1871, Mauser TankGewehr M1918, medicine stick, Mini-mosin,
Model 1888 commission rifle, Modified Mosin-Nagant, Paciencia, rainstick, Remington 700,
salvaged eastern rifle, sawed off Gras, sawed off Martini Henry, Smell-The-Roses, Three Oh Hate,
trail carbine, Training Repeater

### Shotguns (20)

Source: `code/modules/projectiles/guns/ballistic/shotgun.dm`

ballistic fist, Browning Auto-5, caravan rifle, Flair Gun, hand shotgun, hunting shotgun,
lever action shotgun, mare's leg shotgun, Mourning Sunrise, Neostead 2000, Pancor Jackhammer, police shotgun,
Pz87 pump-action shotgun, Riot shotgun, S163 Minotaur shotgun, sawed-off hunting shotgun, trench shotgun,
Venn Family Shotgun, Winchester City-Killer shotgun, Winchester Widowmaker

### Launchers / heavy weapons (14)

Source: `ballistic/{launchers,minigun,magweapon,flamethrower}.dm`

brick launcher, CZ53 personal microgun, CZ53 personal minigun, fatman, grenade rifle, gyrojet pistol,
Hyper-Burst rifle, kinetic speargun, M2 Flamethrower, magnetic rifle, magpistol, multi grenade launcher,
pump grenade launcher, romckit launcher

### Hobo / pipe / craftable (24)

Source: `code/modules/projectiles/guns/hoboguns.dm`, `ballistic/crafting.dm`

advanced 10mm submachine gun, advanced colt rangemaster, advanced m3a1 grease gun, advanced ppsh41,
advanced r91 assault rifle, advanced uzi, Deal with the Devil, destroyer carbine,
enhanced 10mm submachine gun, enhanced colt rangemaster, enhanced m3a1 grease gun, enhanced ppsh-41,
enhanced r91 assault rifle, enhanced uzi, Laser Musket, Obrez, pepperbox gun (10mm), pipe rifle,
Plasma Musket, rebored Winchester, Redwater Special, service rifle (improved), service rifle (masterwork),
shotgun bat

### Flintlock / matchlock (16)

Source: `code/modules/projectiles/guns/ballistic/flintlock.dm`

ancient cavaliers sharpshooting matchlock, ancient fancy matchlock arquebus, ancient hand cannon,
ancient jezail, ancient matchlock arquebus, ancient matchlock carbine, ancient matchlock espingole,
ancient matchlock handgonne, ancient matchlock musketoon, ancient mosquete, ancient musket,
ancient musketoon, ancient spingarda, ancient tanegashima, culverin, flintlock laser pistol

### Bows / crossbows (9)

Source: `code/modules/projectiles/guns/ballistic/bow.dm`

Composite Bow, Light Crossbow, Longbow, Marksman Crossbow, Masterwork Composite Bow, Modern Recurve Bow,
prefall compound bow, Shortbow, yumi bow

### Misc (chem / medical / dart) (9)

Source: `guns/misc/{blastcannon,chem_gun,grenade_launcher,medbeam,syringe_gun}.dm`

blowgun, dart gun, dart pistol, Medical Beamgun, modified syringe gun, pipe gun, rapid syringe gun,
reagent gun, Repeating dart gun

### Modular Sand (modular-only) (2)

Source: `modular_sand/code/modules/projectiles/guns/*` (only entries not already in the core lists)

goat gun, size ray

### Modular Citadel (modular-only) (12)

Source: `modular_citadel/code/modules/*` (rifles, handguns, energy_gun, pumpenergy, spinfusor, toys; only entries not already in the core lists)

AM4-B, AM4-C, Anti-Preservative, CX Flechette Launcher, CX Shredder, foam force stealth pistol,
Foam Force X9, Migraine, particle defender, pump-action particle blaster, pump-action plastic blaster,
Stormhammer Magnetic Cannon

## 4. Assets available in the source

- Gun icons: `icons/fallout/objects/guns/ballistic.dmi` (750 states), `energy.dmi` (482), `longguns.dmi`, `ammo.dmi` (651); `modular_coyote/icons/objects/{automatic,pistols,ancient,rifles,c13ammo}.dmi`; `modular_citadel/icons/obj/guns/*`.
- Inhand/onmob: `icons/fallout/onmob/weapons/guns_lefthand.dmi` / `guns_righthand.dmi` (229/231 states, per-gun) and `64x64_*` for the big guns.
- Audio: 108 `.ogg` files in `sound/f13weapons/` (fire/reload per gun).
- Ammo/magazine sprites exist for the native calibers (see §5.2) if new calibers are ever added.

## 5. Port plan sketch (decisions needed)

### 5.1 Conventions
- Prefix: `_CS` (matches the coyote-bayou clothes already ported with `copyright: coyote-bayou`); prototypes `_CS/Entities/Objects/Weapons/Guns/Fallout/...`, textures `_CS/Objects/Weapons/Guns/...`, audio `_CS/Weapons/Fallout/...`. Alternative: `_PS` if a clean split from the Coyote SS14 content is preferred.
- Prototypes: reuse NF base frames/chambers (`NFBaseWeaponPistolChamber45`, `...SMGChamber35`, `...RifleChamber20/30/60`, `...ShotgunChamber50`, `...RevolverChamber45`), per-gun `Gun` stats and fire modes from the `.dm` (semi/burst/auto, fire rates, burst size), `ItemSlots` mag whitelists, `MagazineVisuals`, `NFWeaponDetails`, `StaticPrice`.
- Sprites: DMI -> RSI with the zTXt/Pillow method from `.ai/guides/porting-from-ss13.md` (frame-major, direction-minor S/N/E/W; `meta.json` credit `coyote-bayou`, CC-BY-SA-3.0).

### 5.2 Caliber mapping (recommended: map to existing NF calibers)
| coyote-bayou | this repo |
|---|---|
| 9mm, .22LR, 4.7mm | .35 auto |
| 10mm, .45 ACP/LC, .44, .357, 14mm | .45 magnum |
| 5.56x45/.223 | .20 rifle |
| 7.62x39, 7.62x51/.308, .30-06 | .30 rifle |
| anti-materiel (Boys / AMR) | .60 |
| 12 gauge | .50 shells |
| 5mm | .20 rifle (or .10 for miniguns) |
| needler / plasma / laser / MFC | energy + new `_CS` projectiles (no caliber) |

Adding native 10mm/.44/.357/5.56/7.62 calibers is the fidelity option but needs cartridges, boxes, magazines, projectiles, tags, loot and sprites for each — much larger than the mapping above.

### 5.3 Suggested phases
1. **Iconic Fallout set (~25)**: AER9/AER12/AER14/LAER, Wattz 1000/2000, AEP7, Solar Scorcher, RCW, plasma pistol/rifle/carbine/Multiplas/Glock 86/Caster, Gamma gun, alien blaster, Tesla cannon, pulse destroyer; 10mm pistol, M1911, N99, Desert Eagle, .44 revolver, cowboy repeater, hunting rifle, R91, assault carbine, marksman carbine, service rifle, M1 Garand, SKS, BAR, combat/trench/hunting shotgun, Auto-5, Pancor Jackhammer, Fatman, CZ53 minigun, pipe rifle, laser/plasma musket.
2. Remaining ballistic (pistols/revolvers/SMGs/rifles/LMGs, needlers, exotic calibers).
3. Hobo/craftable, flintlock/matchlock, bows, launchers, misc.
4. `modular_sand` / `modular_citadel` oddities (AM4, flechette, spinfusor, pump-action particle blaster, size ray, goat gun).

### 5.4 Per-gun checklist
read the `.dm` -> map caliber/fire modes -> extract sprites (+ sounds) -> write the prototype -> wire acquisition (vendor / dungeon case) -> changelog + `.ai/PORTING.md` -> validate (build, YAMLLinter, tests, headless, RSI layout check).

### 5.5 Open decisions
1. Scope: curated Phase 1 (~25) or the whole catalogue in batches?
2. Prefix `_CS` vs `_PS`.
3. Calibers: map to existing (recommended) vs add native 10mm et al.
4. Audio: port `sound/f13weapons` (licensing caveat: Fallout-derived audio) or skip.
5. Magazines: reuse shared NF mags (recommended) vs per-gun mags for capacity fidelity.

## 6. How this list was generated
Scanned every `.dm` under the `coyote-bayou` checkout for top-level `/obj/item/gun/...` type declarations and their explicit `name = "..."`, deduped by type path and then by name across categories, dropped templates / debug / test / cyborg / mounted / toy / practice entries and the names in §2, then grouped by source file. Re-run the scan if the source checkout is updated.

