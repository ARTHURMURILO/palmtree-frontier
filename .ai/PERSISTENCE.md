# PERSISTENCE

> DB code is server-only (`IServerDbManager`); clients never see EF models. Marking data lives inside
> the profile JSON, not in its own tables.

## Schema areas (`Content.Server.Database/Model.cs`)

| Area | Tables/columns | Notes |
|---|---|---|
| Preferences | `Preference` + `Profile` | Profile includes `Markings` (jsonb), nullable `VoiceBark` (Palmtree voice bark, `voice_bark` column), nullable `Genitals` (Palmtree genital organ selection string, `genitals` column, migration `GenitalOrgans`), `Height`/`Width` (`height`/`width` real columns, migration `HeightWidth`), `SnoutAboveHair` (`snout_above_hair` bool column, migration `SnoutAboveHair`) and `LegStyle` (`leg_style` text column, migration `AddLegStyle`, default `Plantigrade`); `Preference.NsfwConsent` is a nullable bool (`nsfw_consent` column, migration `NsfwConsent`; null = age gate unanswered). `CharacterConsentFreetext` is **absent here**. Palmtree bank account numbers are derived at runtime from `NetUserId` + character slot (no DB column) |
| Admin | `Admin`, `AdminRank`, notes, bans, role bans | `LogType` numeric values are load-bearing |
| Playtime | playtime/role time tracking | Frontier/job requirements |
| Whitelist / Patreon | whitelist, patreon tiers | server config dependent |
| Connection log | connection log entries | |
| Consent | only `Preference.NsfwConsent` (account age gate) | the Floof consent tables do not exist in this repo |

Markings are stored as a JSON array of strings in `Profile.Markings` (jsonb column). Each string is
`Marking.ToString()`:

```
markingId@#rrggbb,#rrggbb,...                       # legacy format
markingId@#rrggbb,...@1.25,0.1,-0.2                 # Palmtree: scale,offsetX,offsetY
markingId@#rrggbb,...@1.25,0.1,-0.2@g0.5,1          # Palmtree: + per-color glow levels
markingId@#rrggbb,...@m3@cCustom Name               # Palmtree: + toggle flags (1 self, 2 others) and custom name
```

- `Marking.ParseFromDbString` accepts all forms and clamps values.
- The transform segment is omitted when scale/offset are defaults; the glow segment (prefixed `g`)
  is omitted when all glows are zero; the visibility segment (prefixed `m`) is omitted at the
  defaults (`CanToggleVisible=false`, `OtherCanToggleVisible=false`; undergarments write `@m1`);
  the custom-name segment
  (prefixed `c`) is omitted when empty. Old data stays valid and defaults to self-toggleable.
- Changing the format requires updating `ToString` and `ParseFromDbString` together.

## Migrations

- Two providers: `Content.Server.Database/Migrations/Sqlite` and `.../Postgres`.
- Adding a model change requires migrations for **both**; the repo's tooling (`add-migration.sh`)
  generates both, but verify.
- Migrations added by the port: `VoiceBark` (`voice_bark` text column) and `GenitalOrgans`
  (`genitals` text column; compact `Type:Prototype:Size;...;semen=N` string from
  `GenitalOrganSettings.ToDbString`), `HeightWidth` (`height`/`width` real columns, default 1 —
  the generated migration was patched from default 0 so existing characters do not shrink),
  `AddLegStyle` (`leg_style` text column, default `Plantigrade` — patched from the generated empty
  default; `ServerDbBase` falls back to `Plantigrade` for unparseable values), `NsfwConsent`
  (`nsfw_consent` nullable bool on `preference`; null = age gate unanswered, so existing rows are
  prompted once) and `SnoutAboveHair` (`snout_above_hair` bool on `profile`, default false).
- Migration IDs/order matter; don't renumber or hand-edit old migrations.
- A drift test compares the model to migrations; keep it green.

## Profile export/import

- Export: `HumanoidAppearanceSystem.ToDataNode(HumanoidCharacterProfile)` → `HumanoidProfileExport`
  YAML (`ForkId`, `Version`, `Profile`).
- Import: `FromStream` reads YAML → `SerializationManager.Read<HumanoidProfileExport>` →
  `profile.EnsureValid(session, collection)`.
- Unknown fields (old Floof marking fields, consent blocks) are ignored by the serializer;
  `height`/`width` are supported fields.
- Unknown prototypes (traits/markings/loadouts) are filtered out by `EnsureValid`/`EnsureSpecies`,
  not errors. This is why the port had to ensure ids existed rather than relying on import failure.

## Runtime files

| Path | Contents |
|---|---|
| `data/preferences.db` | SQLite DB (default) |
| `data/replays/` | replay files |
| `data/` saved maps | engine `savegrid`/`savemap` output (never `Resources/`) |
| `server_config.toml` | engine server config |
| `client_config.toml` | client config |

## File-based persistence

- Changelogs: `Resources/Changelog/Palmtree.yml` (this fork's entries, first tab; auto-generated
  from `:cl:` blocks) plus the upstream `Changelog.yml`/`Frontier.yml` history; see
  `.ai/guides/changelogs.md`.
- Map migrations: `Resources/migration.yml` + `Resources/nf_migration.yml` applied on map load.
- Prototype ignore list: `Resources/IgnoredPrototypes/ignoredPrototypes.yml`.

## Ported engines: save/load invariants

The ported atmos/engineering machines are documented in `.ai/systems/atmos-gases-hfr.md`; these
save/load rules have already caused bugs:

- **Gas mixtures are padded, gas ids append-only.** A mixture saved with 11-21 gases is padded to
  `Atmospherics.AdjustedNumberOfGases` (24) by `GasMixture.AfterDeserialization`, so old saves load
  with zero moles in the ten appended slots. Never renumber the `Gas` enum.
- **Persist prototype ids, not `ITileDefinitionManager` ints.** `TurbineAssemblyComponent.FloorTileId`
  and `TurbineFloorRestore.OriginalTileId` are tile prototype id strings; the earlier `int` fields
  were session-local numeric ids and restored the wrong deck after a map reload
  (`TurbineAssemblyTest.FloorRestoreUsesPrototypeIdsAndSurvivesMapReload`).
- **Persisted flags.** `TurbineComponent.Ruined` and `TurbineAssemblyComponent.Deployed` must stay
  `[DataField]`: a saved wreck has to stay ruined (it used to come back intact and explode again),
  and a flatpack-deployed turbine must stay packable after a reload while map-spawned turbines get
  no pack-up verb. The spark/smoke indicators are runtime only and are rebuilt from blade health on
  startup without replaying their popups or sounds.
- **Packed turbine state travels with the flatpack.** `TurbineFlatpackStateComponent` persists blade
  health, ruin flag, stator load and flow rate so a pack/unpack cycle cannot repair or retune a
  turbine for free.
- **HFR part links are runtime only.** `HFRCoreComponent`'s internal mixtures, settings and flags
  persist, but its part links (`ConsoleUid`, `FuelInputUid`, `ModeratorInputUid`, `WasteOutputUid`,
  `CornerUids`) and each part's `CoreUid` do not; they are rebuilt from the anchored parts on
  startup/anchoring. Never add `[DataField]` to a link UID: a reload would resurrect a dead or
  recycled entity id.

## Hazards

- `LogType` values are persisted; never renumber.
- Admin logs can be dropped past `adminlogs.drop_threshold` with no file fallback.
- `database.sqlite_dbpath` default is `preferences.db`; integration tests use in-memory SQLite.
- There is no Postgres integration test in-repo.
