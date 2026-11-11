# Prototype Tutorial: Building Things in Palmtree Station Without Being a Programmer

This guide is for **server staff, mappers, and players** who want to add or change content but do
not write code. You do not need to know C#, and you do not need to understand the game engine. You
only need to be able to edit a text file carefully.

A **prototype** is the game's recipe card for a thing: an item, a machine, a chemical, a floor
tile, a mob, a cargo crate, or even a whole map. The game reads these recipe cards from text files
when it starts. If you can copy a recipe and change a few lines, you can make new content.

> **The one rule that matters most:** new Palmtree content goes in
> `Resources/Prototypes/_PS/...`. Never add new things to `Resources/Prototypes/Entities/...` or
> another server's folder. Those files belong to upstream projects and are replaced during updates.
> Copy ideas from them, not files into them.

This guide uses **real examples from this repository**. Each section shows a small piece of working
code, not a whole file, so you can focus on the part that matters. The final walkthrough is the
exception: it gives you one complete file you can copy and test. You can open the listed files and
see the complete versions.

> **If you are brand new:** read Parts 1–3, then copy the walkthrough in Part 10. Keep Part 12
> open while you test. You do not have to memorise the component catalogue in Part 5; use it as a
> reference when you need a specific behaviour.

---

## Contents

1. [The big picture](#part-1--the-big-picture)
2. [YAML without fear](#part-2--yaml-without-fear)
3. [Your first entity prototype](#part-3--your-first-entity-prototype)
4. [Inheritance: the superpower](#part-4--inheritance-the-superpower)
5. [Components: the behaviour blocks](#part-5--components-the-behaviour-blocks)
6. [Sprites, sound, and looks](#part-6--sprites-sound-and-looks)
7. [Names, descriptions, and translation](#part-7--names-descriptions-and-translation)
8. [Spawn menu, categories, and maps](#part-8--spawn-menu-categories-and-maps)
9. [Other kinds of prototypes](#part-9--other-kinds-of-prototypes)
10. [A full walkthrough: a new soda](#part-10--a-full-walkthrough-a-new-soda)
11. [Testing and validation](#part-11--testing-and-validation)
12. [Troubleshooting](#part-12--troubleshooting)
13. [House rules and best practices](#part-13--house-rules-and-best-practices)
14. [Glossary](#part-14--glossary)
15. [Copy-paste templates](#part-15--copy-paste-templates)
16. [Real examples used in this guide](#part-16--real-examples-used-in-this-guide)

---

## Quick start (for the impatient)

If you only want to make a simple variant of an existing item:

1. Find a working prototype that is close to what you want.
2. Create a new `.yml` file under `Resources/Prototypes/_PS/`.
3. Copy the prototype.
4. Change its `id`, `name`, and `description`.
5. Change one or two component settings.
6. Save the file, restart the server and client, and spawn it with `spawn YourNewID`.

A complete safe example is in [Part 10](#part-10--a-full-walkthrough-a-new-soda). The rest of this
guide explains why each line works, so you can fix problems instead of guessing.

---

# Part 1 — The big picture

## 1.1 What is a prototype?

Think of a prototype as a **recipe card** or a **blueprint**:

- The **ID** is the recipe's unique name. The game uses it; players never see it.
- The **name** and **description** are what players see and read when they examine the thing.
- The **parent** is a recipe you are starting from. It saves you from writing everything again.
- The **components** are the abilities you attach: "can be picked up", "is a drink", "deals
  damage", "glows in the dark", "uses power", and so on.

When the game creates an actual object in the world (an **entity**), it takes a prototype, copies
all of its settings, and attaches the components. The prototype itself is never the object. It is
the instructions for making the object.

A useful comparison:

| Word | Meaning | Example |
|---|---|---|
| **Prototype** | The recipe card | `DrinkIcedTeaCan` |
| **Entity** | An actual object in the world | the iced tea can on the bar counter |
| **Component** | One ability or behaviour | `Item` makes it holdable |
| **System** | The code that runs the behaviour | the code that lets you drink from a cup |

You can edit prototypes. You generally cannot edit systems without being a programmer. That is the
main boundary of this guide.

## 1.2 The four words you need

You will see these words constantly:

1. **Prototype** — a recipe card in a `.yml` file.
2. **ID** — the unique name of the recipe, written in CamelCase with no spaces, like
   `PSBluespaceMiner`.
3. **Parent** — an existing prototype you inherit from.
4. **Component** — a block of settings that gives the thing an ability.

If you understand those four words, the rest of the syntax is mostly punctuation.

## 1.3 How the game turns a text file into a thing

At startup, the game:

1. Scans `Resources/Prototypes/` and every folder inside it.
2. Reads every file whose name ends in **`.yml`**.
3. Reads each `- type: ...` block as one prototype.
4. Builds parent prototypes first, then children, copying settings down the chain.
5. Checks the result for mistakes.
6. Makes the finished prototypes available to the server, the client, the spawn menu, and the
   mapping tools.

This means:

- File and folder names are for **humans**. The game does not care what you call the file, as long
  as it ends in `.yml`.
- The **ID** is what matters to the game. Two prototypes of the same kind cannot share an ID.
- A file can contain one prototype or many. The `- type:` line starts each one.
- Changes are read at startup. After editing, restart the server (and usually the client) before
  testing.

> **`.yml` only.** Files ending in `.yaml` are ignored by the prototype loader. Use `.yml`.

## 1.4 Where prototypes live

All prototype files live under `Resources/Prototypes/`. Folders are organised by topic:

```text
Resources/Prototypes/
├── Entities/          # physical things: items, machines, mobs, walls
├── Stacks/            # stackable materials
├── Reagents/          # chemicals
├── Recipes/           # reactions, construction, fabrication
├── Decals/            # floor and wall decals
├── Tiles/             # floor tiles
├── Catalog/           # cargo, vending machines, uplinks
├── Loadouts/          # character creation gear
└── _PS/               # Palmtree's own content
```

**New Palmtree content goes under `_PS`.** The matching subfolder should describe the thing:

| You are adding | Put it here |
|---|---|
| A new item | `Resources/Prototypes/_PS/Entities/Objects/...` |
| A new machine | `Resources/Prototypes/_PS/Entities/Structures/Machines/...` |
| A new mob | `Resources/Prototypes/_PS/Entities/Mobs/...` |
| A new chemical | `Resources/Prototypes/_PS/Reagents/...` |
| A new reaction | `Resources/Prototypes/_PS/Recipes/Reactions/...` |
| A new stack | `Resources/Prototypes/_PS/Stacks/...` |
| A new tile | `Resources/Prototypes/_PS/Tiles/...` |
| A new decal | `Resources/Prototypes/_PS/Decals/...` |

Real Palmtree files follow this pattern:

```text
Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml
Resources/Prototypes/_PS/Entities/Mobs/NPCs/roomba.yml
Resources/Prototypes/_PS/Reagents/oxidant.yml
Resources/Prototypes/_PS/Recipes/Crafting/Graphs/bots/roomba.yml
```

## 1.5 What you can and cannot do without a coder

You **can** do all of this by editing prototypes:

- Copy an existing thing and change its name, description, numbers, sprite, or sound.
- Make variants of an existing thing (different colour, different damage, different drink).
- Combine existing behaviours by choosing a different parent or adding an existing component.
- Create stacks, reagents, reactions, decals, tiles, cargo products, vending machine stock, and
  loadout entries using existing systems.
- Add a new item to the spawn menu, a vendor, a cargo crate, a loot table, or a map.

You **cannot** do this by editing prototypes alone:

- Invent a brand-new component or a new gameplay mechanic.
- Make a new user interface, a new network message, or a new damage calculation.
- Create new art or audio from nothing (and this repository asks you to **ask first** before
  generating new art).
- Change engine code. `RobustToolbox/` is off-limits.

If a feature needs a new component or new code, ask a programmer. That is not a failure; it is the
normal boundary of content work.

---

# Part 2 — YAML without fear

Prototypes are written in **YAML**. YAML is a way of writing nested lists and settings in a plain
text file. It looks fussy at first, but it has only a few rules. Once you see the pattern, it is
easier than it looks.

## 2.1 The three shapes of YAML

Almost everything in a prototype is one of three shapes:

1. **A setting** — `key: value`
2. **A list** — lines starting with `-`
3. **A group of settings** — a `key:` followed by more-indented settings

That is the whole language in practice.

## 2.2 Indentation is the grammar

YAML uses indentation to show what belongs to what. **Spaces only. Never tabs.** The repository
convention is **two spaces per level**.

Here is a shortened real example with the indentation made visible:

```yaml
- type: entity
  id: DrinkIcedTeaCan
  name: iced tea can
  components:
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
  - type: Item
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

Read it like a family tree:

```text
- type: entity                  <-- this is one prototype in the file
  id: DrinkIcedTeaCan           <-- two spaces in: a setting of the prototype
  name: iced tea can            <-- still two spaces in: another setting
  components:                   <-- a setting whose value is a list
  - type: Sprite                <-- the dash starts one list entry
    sprite: ...rsi              <-- four spaces in: a setting of that entry
  - type: Item                  <-- the next list entry
    sprite: ...rsi
```

Two important details:

- The dash for a list entry is part of the list. The settings under that entry must be indented
  **further than the dash**.
- A setting at the same indentation as another setting belongs to the same parent. A setting that
  is further indented belongs to the thing above it.

If you get this wrong, the game will either ignore the line or refuse to load the file. It will
usually tell you the line number.

### Broken vs fixed

This is **broken** because `sprite` is lined up with `- type: Sprite` instead of being inside it:

```yaml
# Do not copy: the sprite line is not inside the Sprite component.
- type: entity
  id: PSBrokenThing
  components:
  - type: Sprite
  sprite: _PS/Objects/thing.rsi
```

This is **fixed**:

```yaml
# Correct: sprite is indented under the Sprite component.
- type: entity
  id: PSFixedThing
  components:
  - type: Sprite
    sprite: _PS/Objects/thing.rsi
```

## 2.3 Values: text, numbers, true/false, and null

A value can be many things. You do not usually need to add quotes.

| Kind | Real example from the code | What it means |
|---|---|---|
| Text | `name: iced tea can` | A word or sentence |
| Number | `powerLoad: 300` | A whole number |
| Decimal | `price: 5.5` | A number with a decimal point |
| Boolean | `requiresPower: false` | `true` or `false` |
| Null | `interactSuccessString: null` | "Nothing / not set" |
| Colour | `color: "#47f8ff"` | A hex colour code |
| ID / path | `parent: DrinkCanBaseFull` | A reference to another prototype or resource |

The game is **case-sensitive**. `Sprite` and `sprite` are different words. `true` is not the same
as `True` (use lowercase `true` and `false`).

Real example from
`Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml`:

```yaml
- type: WeaponDetector
  scanMode: LethalOnly
```

`WeaponDetector` is the component name. `scanMode` is a setting. `LethalOnly` is a value from a
fixed list defined by the code. You can change `LethalOnly` to another allowed value if you know
the list; you cannot invent a new one.

## 2.4 Lists

A list is a series of entries starting with `-`.

**Block list** — one entry per line:

```yaml
tags:
- DrinkCan
```

**Inline list** — all entries on one line, inside square brackets:

```yaml
parent: [ BaseMachinePowered, ConstructibleMachine ]
```

Real example from `Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml`:

```yaml
- type: entity
  name: baseball bat
  parent: [BaseItem, BaseC1Contraband] # Frontier: BaseMinorContraband<BaseC1Contraband
  id: BaseBallBat
```

Real example of a block list from the same file:

```yaml
  - type: Clothing
    quickEquip: false
    slots:
    - back
    - suitStorage
```

Both styles mean the same thing. The inline style is shorter; the block style is easier to read
when entries are long.

Lists can contain settings, not just single words. This is a real sprite layer list from
`Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml`:

```yaml
  - type: Sprite
    sprite: _PS/Structures/Machines/weapon_detector.rsi
    drawdepth: Doors
    layers:
    - state: base
      map: [ "base" ]
    - state: passive
      map: [ "scan" ]
```

Each `-` entry under `layers` is one picture layer. `state` and `map` are settings of that layer.

## 2.5 Nested settings

Some settings contain other settings. Think of it as folders inside folders. This is a real drink
solution from `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cans.yml`:

```yaml
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
        reagents:
        - ReagentId: Cola
          Quantity: 30
```

The structure is:

```text
SolutionContainerManager
└── solutions
    └── drink
        ├── maxVol: 30
        └── reagents
            └── one entry
                ├── ReagentId: Cola
                └── Quantity: 30
```

Every extra indentation level means "inside the thing above". If you remember that, you can read
almost any prototype.

## 2.6 Comments

A line starting with `#` is a **comment**. The game ignores it. Comments are for humans.

Real example from `Resources/Prototypes/_PS/Entities/Structures/Machines/bluespace_miner.yml`:

```yaml
# Palmtree: port of the SPLURT bluespace miner, adapted to feed a linked ore silo.
# The mining rate, amount and ore weights are all View Variables editable at runtime.
- type: entity
  id: PSBluespaceMiner
```

Comments are useful for explaining **why** a value is what it is. They are also useful for
temporarily disabling a line while testing.

A `#` in the middle of a line is also a comment **if it has a space before it**:

```yaml
  price: 300 # this part is a comment
```

If you want a literal `#` inside text, put the whole value in quotes:

```yaml
  description: "A sign that says #1"
```

## 2.7 Quoting

You do not need quotes for simple text. Use quotes when the text contains characters that YAML
treats specially, or when the value could be mistaken for a number or a true/false value.

Real examples:

```yaml
  color: "#47f8ff"
```

```yaml
        bounds: "-0.45,-0.45,0.45,0.45"
```

```yaml
      map: [ "base" ]
```

```yaml
  description: "A refreshing can of iced tea."
```

Rules of thumb:

- Quotes are optional for ordinary sentences.
- Use quotes if the text contains `#`, `: ` (colon followed by a space), `{`, `}`, `[`, `]`, `,`,
  `&`, `*`, `!`, `|`, `>`, `@`, or a backtick.
- Use quotes if the text starts or ends with a space.
- Use **straight quotes** (`"..."`), not the curly quotes that word processors sometimes insert.
- Single quotes also work, but this repository mostly uses double quotes.

## 2.8 Anchors and aliases (the `&` and `*` symbols)

Some files use a shortcut called a **YAML anchor**. It lets one block of settings be reused later
in the same file.

Real example from `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cans.yml`:

```yaml
      grindable: &grindable
        reagents:
        - ReagentId: Aluminium
          Quantity: 4
        - ReagentId: Iron
          Quantity: 1
```

Later in the same file, another prototype can say:

```yaml
      grindable: *grindable
```

`&grindable` means "remember this block under the name `grindable`". `*grindable` means "put the
remembered block here".

Anchors only work **inside the same file**. If you copy a prototype into a new file, replace
`*grindable` with the full block. For a beginner, it is safe to treat anchors as "the file's own
copy-paste shortcut" and either copy them exactly or write the block out in full.

## 2.9 The mistakes that break files

Most beginner problems are one of these:

| Mistake | Broken | Fixed |
|---|---|---|
| Tabs instead of spaces | a tab before `sprite:` | two or four spaces |
| No space after the colon | `name:Thing` | `name: Thing` |
| Wrong indentation | `sprite:` lined up with `- type:` | indent `sprite:` under the component |
| Unquoted colon in text | `description: Made of: steel` | `description: "Made of: steel"` |
| Curly quotes | `name: “my thing”` | `name: "my thing"` |
| Duplicate setting | two `name:` lines in one prototype | remove one |
| Duplicate ID | two prototypes both named `PSMyThing` | use a unique ID |
| Missing `- type:` | a second prototype starts without `- type:` | add `- type: entity` (or the right kind) |
| Wrong file extension | `mything.yaml` | `mything.yml` |

> **Tip:** If the game says a file failed to load, do not rewrite the whole file. Check the line
> number it gives you, then look at the lines just above it. Indentation errors are usually one line
> above the reported line.

---

# Part 3 — Your first entity prototype

In this part we will read a real, complete, simple entity prototype and understand every line. The
example is the iced tea can from
`Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cans.yml`.

## Step 1: Read a real prototype

Here is the actual prototype:

```yaml
- type: entity
  parent: DrinkCanBaseFull
  id: DrinkIcedTeaCan
  name: iced tea can
  description: A refreshing can of iced tea.
  components:
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
        reagents:
        - ReagentId: IcedTea
          Quantity: 30
      grindable: *grindable
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
  - type: Item
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

That is the entire recipe for the iced tea can. It does not describe every ability the can has.
Those come from `DrinkCanBaseFull` and its parents.

Here is what each top-level line means:

| Line | Meaning |
|---|---|
| `- type: entity` | "This recipe is for a physical thing." |
| `parent: DrinkCanBaseFull` | "Start from the full drink can template." |
| `id: DrinkIcedTeaCan` | "The game's unique name for this recipe." |
| `name: iced tea can` | "The name players see." |
| `description: A refreshing can of iced tea.` | "The text players see when they examine it." |
| `components:` | "Here are the abilities that are different from the parent." |
| `- type: SolutionContainerManager` | "It can hold liquids." |
| `- type: Sprite` | "It has a picture in the world." |
| `- type: Item` | "It can be held." |

## Step 2: Create the file

Make a new file under `_PS` with a clear name, for example:

```text
Resources/Prototypes/_PS/Entities/Objects/Consumable/Drinks/my_new_drink.yml
```

Good file names are lowercase, use underscores, and describe the contents:

```text
my_new_drink.yml
weapon_detector.yml
roomba.yml
```

Avoid spaces in file names. The game does not care, but humans and tools do. Existing Palmtree
files show the pattern:

```text
Resources/Prototypes/_PS/Entities/Mobs/NPCs/roomba.yml
Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml
Resources/Prototypes/_PS/Reagents/oxidant.yml
```

## Step 3: Write the header

Every entity starts with these lines. Only `type` and `id` are truly required, but most real
prototypes also set a parent and a name.

```yaml
- type: entity
  id: PSMyNewDrink
  parent: DrinkCanBaseFull
  name: my new drink
  description: A drink I made for the bar.
```

The order of these lines does not matter to the game. The convention in this repository is usually
`type`, `parent`, `id`, `name`, `description`, then `components`.

### `type`

`type: entity` means "physical thing". Other kinds of prototypes use other types, such as
`reagent`, `stack`, or `tile`. We will cover those in [Part 9](#part-9--other-kinds-of-prototypes).

### `id`

The ID is the game's private name. It must be unique among entity prototypes. Two entity prototypes
cannot both be `PSMyNewDrink`.

Good IDs:

- Are written in **CamelCase** with no spaces: `PSMyNewDrink`, `WeaponDetector`, `MobRoomba`.
- Start with a project prefix when they are new Palmtree content: `PS...`.
- Describe what the thing is, not what it looks like.
- Are stable. Other files may point at them, so renaming an ID later can break vendors, maps,
  loadouts, and recipes.

Players never see the ID. They see `name`.

### `parent`

The parent is the recipe you start from. `DrinkCanBaseFull` already contains:

- the can's physical shape,
- the ability to be picked up,
- the ability to be opened,
- the ability to hold a drink solution,
- the ability to be ground up for materials,
- the "trash when empty" behaviour,
- tags used by other systems,
- the base sprite state.

That is why the iced tea can only needs to say "I am a drink can, but with iced tea and this
picture."

A parent can be a single ID or a list:

```yaml
  parent: DrinkCanBaseFull
```

```yaml
  parent: [ BaseMachinePowered, ConstructibleMachine ]
```

The list form lets you mix behaviours from several templates. We will cover it in
[Part 4](#part-4--inheritance-the-superpower).

### `name`

The name is plain text. It is what players see in the spawn menu, in their hands, and when they
examine the item.

```yaml
  name: iced tea can
```

Do **not** put a translation key here. Entity names are localized automatically using the ID; see
[Part 7](#part-7--names-descriptions-and-translation).

### `description`

The description is the examine text.

```yaml
  description: A refreshing can of iced tea.
```

Write a full sentence. Match the tone of the existing descriptions in the same folder. A
description is optional if the parent already has one, but a new item should usually have its own.

## Step 4: Choose a parent

Choosing a parent is the most important decision. A good parent gives you 90% of the behaviour for
free. A bad parent gives you hidden behaviour you did not want.

Ask: **what is the closest existing thing to what I want?**

| You want to make... | Good starting parent | Why |
|---|---|---|
| A generic hand-held item | `BaseItem` | Gives pickup, physics, sprite, and item basics |
| A drink can | `DrinkCanBaseFull` | Gives the can, opening, solution, and trash behaviour |
| A melee weapon | `BaseBallBat` or another existing weapon | Gives damage, wielding, and item behaviour |
| A piece of clothing | `Clothing` or a specific clothing base | Gives wearability and inventory behaviour |
| A powered machine | `BaseMachinePowered` | Gives power use and machine behaviour |
| A machine that can be built | `BaseMachinePowered` plus `ConstructibleMachine` | Adds construction behaviour |
| A floor light | `PoweredSmallLight` | Gives the light fixture behaviour |
| An NPC | `MobCleanBot` or another mob base | Gives movement, AI, and health |

Real example of a parent list from
`Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml`:

```yaml
- type: entity
  name: baseball bat
  parent: [BaseItem, BaseC1Contraband]
  id: BaseBallBat
  description: A robust baseball bat.
```

`BaseItem` gives it the basic item behaviour. `BaseC1Contraband` gives it the "Class 1
contraband" tag that security and economy systems understand. The bat gets both.

Real example of a parent list from
`Resources/Prototypes/_PS/Entities/Structures/Machines/bluespace_miner.yml`:

```yaml
- type: entity
  id: PSBluespaceMiner
  parent: [ BaseMachinePowered, ConstructibleMachine ]
  name: bluespace miner
  description: Through the power of bluespace, it is capable of producing materials. Link it to an ore silo to deposit its output.
  suffix: Palmtree
```

`BaseMachinePowered` gives it power and machine behaviour. `ConstructibleMachine` makes it
buildable and deconstructable with a machine frame.

### How to find the right parent

1. Find an existing thing that behaves like what you want.
2. Open its `.yml` file and look at its `id`.
3. Search the repository for that ID to see who else uses it.
4. Use the closest **general** parent, not the most specific item, unless you want all of that
   item's quirks.

> **Warning:** A parent is a live link, not a one-time copy. If upstream changes the parent, your
> child changes too. That is usually good (you get bug fixes), but it means you should not inherit
> from an item that is likely to change dramatically.

## Step 5: Name and describe it

Naming rules in this repository:

- Use lowercase for ordinary item names: `iced tea can`, `baseball bat`.
- Keep proper nouns capitalised: `Space Cola can`, `Palmtree cola`.
- Do not include the ID in the name.
- Do not include a suffix like `[Empty]` in `name`; use the `suffix` field instead.
- Keep descriptions short, in the same style as the surrounding items.
- Avoid jokes that only make sense to programmers. Players read these.

Real example:

```yaml
- type: entity
  parent: DrinkCanBaseFull
  id: DrinkColaCan
  name: Space Cola can
  description: A sweet, carbonated soft drink.
```

## Step 6: Add components

The `components:` block is a list. Each entry starts with `- type:` and the name of a component.

Real example from the iced tea can:

```yaml
  components:
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
        reagents:
        - ReagentId: IcedTea
          Quantity: 30
      grindable: *grindable
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
  - type: Item
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

Three things to notice:

1. The component name is **CamelCase**: `SolutionContainerManager`, `Sprite`, `Item`.
2. The settings inside are **camelCase** or lowercase: `maxVol`, `sprite`, `reagents`.
3. The component order does not matter. The game reads them by name. Put them in an order that is
   easy for a human to read: appearance first, then physical behaviour, then extras.

### Why are there only three components?

Because the parent already has the rest. The child only lists components it wants to **change** or
**add**.

- `SolutionContainerManager` is listed because the child changes the contents from the parent's
  default to iced tea.
- `Sprite` is listed because the child changes the picture.
- `Item` is listed because the child changes the in-hand picture.

Everything else — the can's shape, opening behaviour, trash behaviour, tags — is inherited.

### Adding a component that the parent does not have

Just add it to the list:

```yaml
  - type: Tag
    tags:
    - Cola
    - DrinkCan
```

Real example from `DrinkColaCan`:

```yaml
  - type: Tag
    tags:
    - Cola
    - DrinkCan
```

This adds a `Tag` component. Tags are labels that other systems can search for. They do not do
anything by themselves; they are hooks.

### Overriding a component the parent already has

List the component again and write only the settings you want to change:

```yaml
  - type: WeaponDetector
    scanMode: LethalOnly
```

The game merges your settings onto the inherited component. Everything you do not mention stays as
the parent had it.

This is the real pattern used by
`Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml`:

```yaml
- type: entity
  parent: PSWeaponDetector
  id: PSWeaponDetectorLethal
  suffix: Lethal only
  description: A walk-through scanner gate that beeps when someone carrying a lethal gun passes under it. Toy guns don't count.
  components:
  - type: WeaponDetector
    scanMode: LethalOnly
```

Only `scanMode` and `description` changed. The sprite, power use, placement, and scanner behaviour
all come from `PSWeaponDetector`.

## Step 7: Point it at a sprite

The `Sprite` component controls what the thing looks like in the world. The minimum is a path to
an `.rsi` folder:

```yaml
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

The path is relative to `Resources/Textures/`. The example above points to:

```text
Resources/Textures/Objects/Consumable/Drinks/ice_tea_can.rsi/
```

A `.rsi` is a **folder**, not a file. It contains a `meta.json` and one or more PNG images. The
`state` setting chooses an image inside it.

The iced tea can does not set `state` because its parent, `DrinkBase`, already sets:

```yaml
  - type: Sprite
    state: icon
```

Real example from `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_base.yml`:

```yaml
- type: entity
  id: DrinkBase
  components:
  - type: Sprite
    state: icon
```

If your parent does not set a state, add one yourself:

```yaml
  - type: Sprite
    sprite: _PS/Objects/MyThing.rsi
    state: icon
```

We will cover RSI folders, states, and layers in [Part 6](#part-6--sprites-sound-and-looks).

## Step 8: Give it behaviour

Behaviour comes from components. If you want the thing to do something, it needs a component that
implements that behaviour. You cannot write a sentence in the YAML and have the game understand it.

Real examples of behaviour components:

### Melee damage

From `Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml`:

```yaml
  - type: MeleeWeapon
    wideAnimationRotation: -135
    damage:
      types:
        Blunt: 10
        Structural: 5
    soundHit:
      collection: MetalThud
```

This means: "When used to hit something, do 10 Blunt damage and 5 Structural damage, and play a
sound from the `MetalThud` sound collection."

### Wielding bonus

From the same file:

```yaml
  - type: Wieldable
  - type: IncreaseDamageOnWield
    damage:
      types:
        Blunt: 5
        Structural: 10
```

`Wieldable` lets the item be used with two hands. `IncreaseDamageOnWield` adds extra damage when it
is wielded. Both are existing components; you are only tuning numbers.

### Clothing

From the same file:

```yaml
  - type: Clothing
    quickEquip: false
    slots:
    - back
    - suitStorage
```

`Clothing` makes the item wearable. `slots` lists the body slots it can go in. `quickEquip: false`
means clicking it does not instantly equip it.

### Power use

From `Resources/Prototypes/_PS/Entities/Structures/Machines/bluespace_miner.yml`:

```yaml
  - type: ApcPowerReceiver
    powerLoad: 300
```

`ApcPowerReceiver` makes the machine draw power from an APC (the power box on the wall).
`powerLoad: 300` means 300 watts.

### Machine board

From the same file:

```yaml
  - type: Machine
    board: PSBluespaceMinerCircuitboard
```

`Machine` marks it as a machine. `board` points to the machine board entity that is used to build
it.

### Destructible

From the same file:

```yaml
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 300
      behaviors:
      - !type:PlaySoundBehavior
        sound:
          collection: MetalBreak
      - !type:ChangeConstructionNodeBehavior
        node: machineFrame
      - !type:DoActsBehavior
        acts: ["Destruction"]
```

This says: "After 300 damage, play a metal break sound, turn into a machine frame, and mark it as
destroyed." The `!type:` words are code-defined behaviours. Copy them exactly; do not invent them.
We will explain them in [Part 5](#part-5--components-the-behaviour-blocks).

## Step 9: Put it in the spawn menu

A normal, non-abstract entity with a name appears in the spawn menu automatically. But there are
some optional fields that control how it appears.

### `suffix`

`suffix` adds a short label in **development menus only** — like the spawn menu or the map editor.
It does not change the item's real name.

Real example from `DrinkCanBaseFull`:

```yaml
- type: entity
  abstract: true
  parent: [ DrinkBaseMaterialMetal, DrinkBase, DrinkBaseOpenable, DrinkVisualsOpenable ]
  id: DrinkCanBaseFull
  suffix: Full
```

The spawn menu shows this as "full drink can [Full]" or similar. The actual item name is still
whatever the parent name is.

Real example of a suffix on a variant:

```yaml
- type: entity
  parent: PSWeaponDetector
  id: PSWeaponDetectorLethal
  suffix: Lethal only
```

The suffix helps you tell the base detector and the "lethal only" variant apart in the spawn menu.

### `categories`

`categories` puts the prototype into spawn-menu or mapping groups. It is an inline list of category
IDs.

Real example from `Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml`:

```yaml
- type: entity
  name: knockback stick
  parent: BaseBallBat
  id: WeaponMeleeKnockbackStick
  description: And then he spleefed all over.
  categories: [ DoNotMap ]
```

`DoNotMap` means "this should not be placed on a map." The project has a test that checks maps for
entities in this category.

Another common category is `HideSpawnMenu`, which hides a real entity from the spawn menu. Real
example from `Resources/Prototypes/_PS/ShipCombat/weapons.yml`:

```yaml
- type: entity
  parent: BaseBullet
  id: ShipTurretBolt
  name: turret bolt
  categories: [ HideSpawnMenu ]
```

A bullet is a real entity, but you should not spawn it by hand, so it is hidden.

### `abstract`

`abstract: true` means "this is a template, not a real thing." Abstract prototypes are hidden from
the spawn menu and are meant to be used as parents.

Real example from `DrinkCanBaseFull`:

```yaml
- type: entity
  abstract: true
  parent: [ DrinkBaseMaterialMetal, DrinkBase, DrinkBaseOpenable, DrinkVisualsOpenable ]
  id: DrinkCanBaseFull
```

You would not spawn `DrinkCanBaseFull` itself. You spawn a child like `DrinkColaCan` or
`DrinkIcedTeaCan`.

> **Abstract vs HideSpawnMenu:** `abstract: true` means "do not use this as a real object."
> `categories: [ HideSpawnMenu ]` means "this is a real object, but keep it out of the spawn menu."
> They look similar in the menu, but they mean different things.

### `placement`

`placement` controls how the thing snaps or rotates when an admin or mapper places it. It is mostly
for machines and structures.

Real example from `Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml`:

```yaml
- type: entity
  parent: BaseMachinePowered
  id: PSWeaponDetector
  name: weapon detector
  description: A walk-through scanner gate that beeps when someone carrying a gun passes under it.
  placement:
    mode: SnapgridCenter
```

`SnapgridCenter` makes the placement preview snap to the centre of a grid tile. Items usually do
not need placement settings.

## Step 10: Test it

The fastest test is in-game:

1. Start your development server and client.
2. If you have admin permissions, open the admin menu with **F7**. (Otherwise, ask an admin to
   spawn the item for you.)
3. Go to the **Adminbus** tab.
4. Click **Spawn Entities**.
5. Search for your item by **name**, **ID**, or **suffix**.
6. Click it, then click on the ground to place it.

You can also use the console:

1. Press the **tilde/backtick** key (`` ` ``) to open the console.
2. Type `spawn YourPrototypeID` and press Enter.
3. The item spawns at your character's position.

For the iced tea can:

```text
spawn DrinkIcedTeaCan
```

Check:

- Does it appear in the spawn menu?
- Does it have the right name?
- Does the description show when you examine it?
- Is the sprite visible and the right size?
- Can you pick it up?
- Does it do what you expect when used?

If any of these fail, go to [Part 12 — Troubleshooting](#part-12--troubleshooting).

---

# Part 4 — Inheritance: the superpower

Inheritance is the reason a 20-line prototype can create a complex machine. You write only what is
different; the parent supplies the rest.

## 4.1 The template idea

A child prototype says:

```yaml
- type: entity
  parent: SomeParent
  id: MyChild
  # only the differences go here
```

The game builds the parent first, then copies its settings into the child, then applies the
child's changes on top.

Think of it like a form:

- The parent fills in most of the form.
- The child crosses out a few answers and writes new ones.
- The final form is the child.

The parent is not modified. Other children of the same parent are not affected.

## 4.2 Abstract parents

A parent that is only meant to be a template usually has `abstract: true`. This is a convention
that keeps it out of the spawn menu and tells humans "do not spawn this."

Real example from `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cans.yml`:

```yaml
- type: entity
  abstract: true
  parent: [ DrinkBaseMaterialMetal, DrinkBase, DrinkBaseOpenable, DrinkVisualsOpenable ]
  id: DrinkCanBaseFull
  suffix: Full
```

`DrinkCanBaseFull` is the shared template for every full drink can. It is not a specific drink. It
combines four parents:

- `DrinkBaseMaterialMetal` — it is made of metal.
- `DrinkBase` — it is a drink container.
- `DrinkBaseOpenable` — it can be opened.
- `DrinkVisualsOpenable` — it has open/closed visuals.

The child then only says what makes it unique:

```yaml
- type: entity
  parent: DrinkCanBaseFull
  id: DrinkIcedTeaCan
  name: iced tea can
  description: A refreshing can of iced tea.
  # ...
```

## 4.3 Overriding one setting

The simplest kind of child changes one setting.

Real example from `Resources/Prototypes/_PS/Entities/Structures/Lighting/colored_bulb_lights.yml`:

```yaml
- type: entity
  id: PoweredSmallLightCyan
  parent: PoweredSmallLight
  suffix: Cyan
  components:
  - type: PoweredLight
    hasLampOnSpawn: LightBulbCrystalCyan
  - type: PointLight
    color: "#47f8ff"
```

This child:

- inherits the whole `PoweredSmallLight` fixture,
- changes the bulb that spawns inside it,
- changes the light colour.

Everything else — the sprite, the power behaviour, the construction — is inherited.

Another real example, this time changing a single field on a single component:

```yaml
- type: entity
  parent: PSWeaponDetector
  id: PSWeaponDetectorLethal
  suffix: Lethal only
  description: A walk-through scanner gate that beeps when someone carrying a lethal gun passes under it. Toy guns don't count.
  components:
  - type: WeaponDetector
    scanMode: LethalOnly
```

The only behavioural change is `scanMode: LethalOnly`. The description changed too, because the
player-facing text should explain the difference.

## 4.4 Adding components

A child can add a component the parent does not have.

Real example from `Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml`:

```yaml
- type: entity
  name: incomplete baseball bat
  parent: BaseItem
  id: IncompleteBaseBallBat
  description: A few planks of wood stuck together.
  components:
  - type: Sprite
    sprite: Objects/Weapons/Melee/incomplete_bat.rsi
    state: icon
  - type: Item
    size: Normal
    shape:
    - 0,0,0,3
    storedSprite:
      state: storage
      sprite: Objects/Weapons/Melee/incomplete_bat.rsi
    sprite: Objects/Weapons/Melee/incomplete_bat.rsi
  - type: Construction
    graph: WoodenBat
    node: incompleteBat
```

The parent `BaseItem` has the basic item behaviour. The child adds:

- a custom sprite,
- a custom item shape,
- a `Construction` component, which links it to the wooden bat crafting graph.

`Construction` is a new ability. The child did not have to re-create pickup, physics, or the item
component.

## 4.5 Replacing lists

Most list settings are **replaced as a whole**, not merged. If you want the parent's values plus
your own, you must write all of them again.

Real example from `DrinkCanBaseFull`:

```yaml
  - type: Tag
    tags:
    - DrinkCan
```

The child `DrinkColaCan` re-declares the whole list:

```yaml
  - type: Tag
    tags:
    - Cola
    - DrinkCan
```

If lists were merged, the child would only need to add `Cola`. Because lists are replaced, the child
writes `DrinkCan` again to keep it.

> **Rule of thumb:** When you override a list, copy the parent's list first, then add or remove
> entries. Then check in-game. A few fields are specially marked to merge instead of replace, but
> you cannot tell that from the YAML alone. Re-declaring the full list is the safe habit.

## 4.6 Multiple parents

A child can have more than one parent:

```yaml
  parent: [ DrinkBaseOpenableOpen, DrinkColaCan ]
```

Real example from `DrinkColaCanEmpty`:

```yaml
- type: entity
  parent: [ DrinkBaseOpenableOpen, DrinkColaCan ]
  id: DrinkColaCanEmpty
  suffix: Empty
  components:
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
      grindable: *grindable
  - type: Tag
    tags:
    - Cola
    - DrinkCan
    - Trash
```

This combines:

- `DrinkBaseOpenableOpen` — the "already opened" behaviour,
- `DrinkColaCan` — the cola name, description, and look.

The child then changes the solution to start empty and adds the `Trash` tag. The original file uses
the `*grindable` anchor from earlier in the same file; it is shown expanded here so the example is
self-contained. If you copy this into a new file, keep the expanded `grindable` block.

### Which parent wins?

When two parents set the same setting, the **first parent in the list wins**. The second parent
only fills in things the first parent did not provide.

Practical advice:

- Put the most specific or most important parent first.
- Put the general behaviour parent second.
- If a value is surprising, check both parents.

## 4.7 When not to inherit

Inheritance is powerful, but it is not always the right tool.

Do not inherit from a very specific item just because it is close. That item may bring hidden
behaviour you do not want:

- a construction recipe,
- a contraband tag,
- a clothing slot,
- a tool quality,
- a price,
- a trash-on-empty behaviour.

The baseball bat, for example, inherits `BaseItem` and `BaseC1Contraband`, and then adds
`Construction`, `Tool`, `Clothing`, `UseDelay`, and `PhysicalComposition`. If you inherit from
`BaseBallBat` for a completely different wooden object, you get all of that too.

Use a general parent (`BaseItem`, `BaseMachinePowered`, `BaseStructure`) when you want a clean
start. Use a specific parent when you genuinely want its full behaviour.

---

# Part 5 — Components: the behaviour blocks

## 5.1 What components are

A component is a **module of behaviour**. The game has a component for almost everything:

- `Item` — can be held.
- `Sprite` — has a picture.
- `MeleeWeapon` — can hit things.
- `Clothing` — can be worn.
- `SolutionContainerManager` — can hold liquids or chemicals.
- `ApcPowerReceiver` — uses station power.
- `PointLight` — emits light.
- `Storage` — can store other items.
- `HTN` — has an AI brain.
- `Construction` — can be built or deconstructed.

You do not create components in YAML. You **configure existing ones**. The component names are
defined by the C# code; a typo or an invented name will fail validation.

## 5.2 Reading a component block

A component block has this shape:

```yaml
  - type: ComponentName
    settingOne: value
    settingTwo:
      nestedSetting: value
    listSetting:
    - entryOne
    - entryTwo
```

The `- type:` line names the component. Everything indented under it is that component's settings.

Real example from `Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml`:

```yaml
  - type: MeleeWeapon
    wideAnimationRotation: -135
    damage:
      types:
        Blunt: 10
        Structural: 5
    soundHit:
      collection: MetalThud
```

This breaks down as:

| Setting | Meaning |
|---|---|
| `wideAnimationRotation: -135` | How the character swings it |
| `damage.types.Blunt: 10` | 10 Blunt damage per hit |
| `damage.types.Structural: 5` | 5 Structural damage per hit |
| `soundHit.collection: MetalThud` | Play a random sound from the `MetalThud` collection |

You do not need to know what every setting does to copy a working block. Change one value at a
time and test.

## 5.3 A tour of common components

Here is a quick reference. Most entries contain a real snippet; the index in Part 16 lists the
source files if you want to see the complete blocks.

### `Sprite` — appearance

```yaml
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

```yaml
  - type: Sprite
    sprite: _PS/Structures/Machines/weapon_detector.rsi
    drawdepth: Doors
    layers:
    - state: base
      map: [ "base" ]
    - state: passive
      map: [ "scan" ]
```

Use it for: the world icon, the floor picture, the machine body, the light fixture.

### `Item` — can be held

```yaml
  - type: Item
    size: Normal
    shape:
    - 0,0,0,3
    storedSprite:
      state: storage
      sprite: Objects/Weapons/Melee/incomplete_bat.rsi
    sprite: Objects/Weapons/Melee/incomplete_bat.rsi
```

Use it for: anything a player can pick up. `size` controls the inventory size. `shape` controls the
grid footprint in storage. `sprite` is the in-hand look. `storedSprite` is the optional picture
shown inside a storage container.

### `MeleeWeapon` — can hit things

```yaml
  - type: MeleeWeapon
    wideAnimationRotation: -135
    damage:
      types:
        Blunt: 10
        Structural: 5
    soundHit:
      collection: MetalThud
```

Use it for: bats, knives, tools, anything that does melee damage.

### `Wieldable` and `IncreaseDamageOnWield` — two-handed bonus

```yaml
  - type: Wieldable
  - type: IncreaseDamageOnWield
    damage:
      types:
        Blunt: 5
        Structural: 10
```

Use it for: weapons that get better when used with both hands.

### `Clothing` — can be worn

```yaml
  - type: Clothing
    quickEquip: false
    slots:
    - back
    - suitStorage
```

Use it for: hats, shoes, jumpsuits, backpacks, and armour. `slots` must use valid slot names.

### `SolutionContainerManager` — can hold liquids or chemicals

```yaml
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
        reagents:
        - ReagentId: Cola
          Quantity: 30
```

Use it for: drinks, beakers, syringes, fuel tanks, and anything that holds reagents.

- `solutions` is a collection of named solutions. The name `drink` is used by the parent's
  drinking components.
- `maxVol` is the capacity in units.
- `reagents` is the starting contents.
- `ReagentId` points to a **reagent prototype**, not an entity prototype.
- `Quantity` is the number of units.

To make a variant start empty, re-declare the solution with only `maxVol`. If the parent had other
solutions (for example `grindable`), re-declare those too, or they will be left out of the child's
`solutions` block. The real empty-cola prototype is a working example of this.

### `SolutionTransfer` — can pour or transfer

```yaml
  - type: SolutionTransfer
    canChangeTransferAmount: true
    maxTransferAmount: 15
```

Use it with `SolutionContainerManager` to let players move liquid between containers.

### `TrashOnSolutionEmpty` — becomes trash when empty

```yaml
  - type: TrashOnSolutionEmpty
    solution: drink
```

The component name is self-explanatory: when the named solution is empty, the entity is treated as
trash.

### `Extractable` and `PhysicalComposition` — grinding and recycling

```yaml
  - type: Extractable
    grindableSolutionName: grindable
```

```yaml
  - type: PhysicalComposition
    materialComposition:
      Steel: 50
```

`Extractable` lets a grinder extract a solution. `PhysicalComposition` says what materials the item
is made of when recycled.

### `Tag` — labels for other systems

```yaml
  - type: Tag
    tags:
    - Cola
    - DrinkCan
    - Trash
```

Tags do not do anything on their own. They let other systems ask "is this a drink can?" or "is this
trash?" You can add tags that already exist; you generally should not invent new tags unless a
coder adds them to the tag list.

### `ApcPowerReceiver` — uses station power

```yaml
  - type: ApcPowerReceiver
    powerLoad: 300
```

`powerLoad` is the power draw in watts. The machine needs to be on a powered grid and usually
needs an APC.

### `Machine` — is a machine

```yaml
  - type: Machine
    board: PSBluespaceMinerCircuitboard
```

`board` is the entity ID of the machine board used to construct it.

### `WiresPanel` and `WiresVisuals` — has wires

```yaml
  - type: WiresVisuals
  - type: WiresPanel
```

Use these on machines that should have a maintenance panel and wire layout.

### `Destructible` — can be damaged and destroyed

```yaml
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 300
      behaviors:
      - !type:PlaySoundBehavior
        sound:
          collection: MetalBreak
      - !type:ChangeConstructionNodeBehavior
        node: machineFrame
      - !type:DoActsBehavior
        acts: ["Destruction"]
```

This is an advanced component. It says what happens at certain damage thresholds. Copy a working
block from a similar machine rather than writing one from scratch.

### `PointLight` — emits light

```yaml
  - type: PointLight
    color: "#47f8ff"
```

`color` is a hex colour. Other common settings are `energy`, `radius`, and `enabled`.

### `PoweredLight` — is a light fixture

```yaml
  - type: PoweredLight
    hasLampOnSpawn: LightBulbCrystalCyan
```

`hasLampOnSpawn` is the bulb entity that starts inside the fixture.

### `Appearance` and `GenericVisualizer` — changes its look based on state

```yaml
  - type: Appearance
  - type: GenericVisualizer
    visuals:
      enum.PowerDeviceVisuals.Powered:
        active:
          True: { visible: true }
          False: { visible: false }
```

This is advanced. `Appearance` lets the entity have visual states. `GenericVisualizer` maps a state
to a sprite layer. The `enum...` name comes from code; copy it exactly from a working prototype.

### `Transform` and `Rotatable` — placement and rotation

```yaml
  - type: Transform
    noRot: false
  - type: Rotatable
    rotateWhileAnchored: true
```

Use these on machines and structures that should rotate. `noRot: false` means it is allowed to
rotate.

### `UseDelay` — cooldown between uses

```yaml
  - type: UseDelay
    delay: 1
```

`delay` is in seconds.

### `StaticPrice` — sell price

```yaml
  - type: StaticPrice
    price: 300
```

`price` is in the game's currency units. This is used by economy systems, not by players directly.

### `Contraband` — contraband class

```yaml
  - type: Contraband
    severity: Class1
    allowedDepartments: [ Civilian, Security, Command, Frontier, CentralCommand ]
```

This marks the item as contraband and lists which departments may legally carry it. Copy the
department names exactly from a working example.

### `Storage` and `ContainerContainer` — can store items

```yaml
  - type: Storage
  - type: ContainerContainer
    containers:
      storagebase: !type:Container
        ents: []
```

Use this on backpacks, boxes, and crates. `ContainerContainer` defines the storage container.
`ents: []` means it starts empty.

### `HTN` — AI brain

```yaml
  - type: HTN
    rootTask:
      task: RoombaIdleCompound
```

`HTN` is the AI system. `rootTask` points to a task prototype defined by code. Non-coders should
copy an existing AI block rather than inventing one.

### `MovementSpeedModifier` — movement speed

```yaml
  - type: MovementSpeedModifier
    baseWalkSpeed: 1.6
    baseSprintSpeed: 1.8
```

The default human values are `2.5` walking and `4.5` sprinting. Use small changes and test; a
number that looks reasonable can still make a mob much too fast or too slow.

## 5.4 Component order and duplicates

- **Order does not matter.** The game reads components by name.
- **Do not list the same component twice in one prototype.** The validator will report a duplicate.
- A child listing a component that the parent already has is **not** a duplicate. It is an
  override, and it merges with the parent's version.
- You can remove an inherited component only by choosing a different parent. There is no
  general "remove this component" switch in YAML. If you need a clean start, pick a simpler parent.

## 5.5 Special `!type:` tags

You will see lines like this:

```yaml
        !type:DamageTrigger
```

or:

```yaml
      storagebase: !type:Container
```

The `!type:` word is a **YAML tag**. It tells the game which C# class to use for that value. It is
part of the code's vocabulary.

Rules:

- Copy `!type:` lines exactly from a working prototype.
- Do not invent a new `!type:` name.
- Do not add or remove spaces around the `!`.
- If you do not know what a `!type:` block does, do not edit it. Change a number elsewhere, or ask
  a coder.

## 5.6 If the component you need does not exist

Prototypes can only configure components that the code already has. If you want a genuinely new
behaviour — a new kind of gun, a new machine, a new status effect — a programmer has to add a
component and a system first.

That is not a limitation of your creativity. It is the boundary between **content** (YAML) and
**code** (C#).

---

# Part 6 — Sprites, sound, and looks

## 6.1 What an RSI is

An `.rsi` is a folder that contains:

- `meta.json` — a list of the images, their directions, animation delays, and license info.
- one or more PNG files — the actual pictures.

The path in a prototype points to the folder, not to a PNG:

```yaml
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

This resolves to:

```text
Resources/Textures/Objects/Consumable/Drinks/ice_tea_can.rsi/
```

The `state` setting names one image inside the folder:

```yaml
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
    state: icon
```

If the state is missing from `meta.json`, the sprite will not show correctly. The linter may catch
some missing states, but not all; always check visually.

## 6.2 Reading a `meta.json`

Here is a simplified, real-shaped example based on
`Resources/Textures/Objects/Consumable/Drinks/ice_tea_can.rsi/meta.json`. The `...` in the
copyright line is a placeholder for the real credit; a real file contains the full text:

```json
{
  "version": 1,
  "license": "CC-BY-SA-3.0",
  "copyright": "https://github.com/discordia-space/CEV-Eris/...",
  "size": {
    "x": 32,
    "y": 32
  },
  "states": [
    {
      "name": "icon"
    },
    {
      "name": "icon_open"
    },
    {
      "name": "inhand-right",
      "directions": 4
    },
    {
      "name": "inhand-left",
      "directions": 4
    }
  ]
}
```

What the fields mean:

| Field | Meaning |
|---|---|
| `version` | RSI format version. Leave it as `1` |
| `license` | The license of the art. Keep the original license when copying |
| `copyright` | Who made the art, or where it came from |
| `size` | The size of one frame in pixels. Most SS14 art is 32×32 |
| `states` | The list of images in the folder |
| `name` | The state name used by `state:` in YAML |
| `directions` | How many facing directions the image contains. `4` is common for in-hand and mob sprites |
| `delays` | Animation frame delays, in seconds. Used for animated states |

> **Never remove the license or copyright lines.** They are how the project keeps track of where
> art came from. If you copy art from another folder, copy its `meta.json` too and keep the credits.

## 6.3 Sprite states

A simple item usually has an `icon` state. That is the picture shown on the floor, in the inventory,
and in the spawn menu.

A drink can also has `icon_open`, `inhand-right`, and `inhand-left`:

- `icon` — closed can.
- `icon_open` — opened can.
- `inhand-right` — how it looks in the right hand.
- `inhand-left` — how it looks in the left hand.

The item prototype does not list all of these. The parent `DrinkVisualsOpenable` and the `Item`
component use them automatically. The child only changes the folder:

```yaml
  - type: Sprite
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
  - type: Item
    sprite: Objects/Consumable/Drinks/ice_tea_can.rsi
```

## 6.4 Sprite layers

A sprite can be built from several layers. This is how machines show a base, an active light, an
open panel, and so on.

Real example from `Resources/Prototypes/_PS/Entities/Structures/Machines/bluespace_miner.yml`:

```yaml
  - type: Sprite
    sprite: _PS/Structures/Machines/bluespace_miner.rsi
    snapCardinals: true
    layers:
    - state: miner
      map: ["base"]
    - state: miner_on
      map: ["active"]
      shader: unshaded
      visible: false
    - state: miner_open
      map: ["enum.WiresVisualLayers.MaintenancePanel"]
      visible: false
```

What this means:

| Setting | Meaning |
|---|---|
| `sprite` | The RSI folder |
| `snapCardinals` | Snap the sprite to cardinal directions when placed |
| `layers` | A list of images drawn on top of each other |
| `state` | The image name inside the RSI |
| `map` | A name for the layer, so code can find and change it |
| `shader: unshaded` | Draw the layer without lighting, so it glows in the dark |
| `visible: false` | Start hidden; code shows it later |

For a simple item, you usually do not need layers. For a machine with an on/off light, you do.

## 6.5 Item icons vs world icons vs in-hand icons

There are three different pictures to think about:

1. **World icon** — the `Sprite` component. This is what you see on the floor or on a table.
2. **In-hand icon** — the `Item` component's `sprite`. This is what you see when a character holds
   it.
3. **Storage icon** — the `Item` component's `storedSprite`. This is an optional picture used inside
   a backpack or box.

Real example from the baseball bat:

```yaml
  - type: Item
    size: Normal
    shape:
    - 0,0,0,3
    storedSprite:
      state: storage
      sprite: Objects/Weapons/Melee/baseball_bat.rsi
```

`storedSprite` is optional. If you leave it out, the game uses the world icon or a default shape.

## 6.6 Sound

Sound can be a single file or a collection.

**Single file** — from `BaseItem`:

```yaml
  - type: EmitSoundOnLand
    sound:
      path: /Audio/Effects/drop.ogg
      params:
        volume: 2
```

- `path` points to one `.ogg` file.
- `params` are optional settings like volume.
- A path starting with `/` is relative to `Resources/`. `/Audio/Effects/drop.ogg` means
  `Resources/Audio/Effects/drop.ogg`.

**Collection** — from the baseball bat:

```yaml
  - type: MeleeWeapon
    soundHit:
      collection: MetalThud
```

`collection` points to a `soundCollection` prototype. A collection is a list of sounds; the game
picks one at random each time.

Real `soundCollection` from `Resources/Prototypes/_PS/SoundCollections/weapons.yml`:

```yaml
- type: soundCollection
  id: Phaser
  files:
  - /Audio/_PS/Weapons/Phaser/phaser1.ogg
  - /Audio/_PS/Weapons/Phaser/phaser2.ogg
```

Use a collection when you want variety. Use a single `path` when the sound should always be the
same.

## 6.7 Asset house rules

- New Palmtree art goes in `Resources/Textures/_PS/...`.
- New Palmtree audio goes in `Resources/Audio/_PS/...`.
- Do not add new files to upstream asset folders. Copy the file to `_PS` and point the prototype at
  the copy.
- Keep the `meta.json` license and copyright information.
- Use `.ogg` for audio.
- Use `.png` for sprites.
- If you need new art, ask the maintainer first. Do not generate new art on your own.

---

# Part 7 — Names, descriptions, and translation

## 7.1 Entity names are plain text

For **entity** prototypes, `name` and `description` are the English text itself:

```yaml
  name: iced tea can
  description: A refreshing can of iced tea.
```

They are not translation keys. The game creates an automatic translation key from the entity ID,
so translators can override the text later. The integration test
`Content.IntegrationTests/Tests/Localization/EntityPrototypeLocalizationTest.cs` specifically
checks that entity names and descriptions are **not** localization keys.

Like other settings, `name` and `description` are inherited. If a child does not set them, the game
walks up its parents (first parent first) and uses the first name or description it finds.
`DrinkColaCanEmpty` relies on this: it inherits the "Space Cola can" name from `DrinkColaCan` and
only adds a development suffix.

Do:

```yaml
  name: atmospheric alerts computer
  description: Used to access the station's atmospheric automated alert system.
```

Do not:

```yaml
  # Do not do this: entity names are not loc keys.
  name: ent-ComputerAlert
```

## 7.2 How translation overrides work

If the game finds a Fluent (`.ftl`) entry named `ent-<ID>`, it uses that instead of the YAML text.
This is how translators change the game's language without editing every prototype.

Real example from `Resources/Locale/en-US/_NF/prototypes/entities/structures/machines/computers/computers.ftl`:

```ftl
ent-ComputerAlert = atmospheric alerts computer
    .desc = Used to access the local atmospheric automated alert system.
```

- `ent-ComputerAlert` overrides the entity's **name**.
- `.desc` overrides the entity's **description**.
- `.suffix` can override the development suffix.

So the YAML says one thing, and the `.ftl` file says another. The `.ftl` wins.

For a new entity, you do not have to add an `.ftl` entry. The YAML text is used by default. Add one
only if you need a translation override or another language.

## 7.3 `suffix`

`suffix` is for development menus, not for players. It is shown in the spawn menu and map editor to
tell variants apart.

Real example:

```yaml
- type: entity
  parent: PSWeaponDetector
  id: PSWeaponDetectorLethal
  suffix: Lethal only
```

The item's actual name does not become "weapon detector lethal only". The suffix is extra
information in the editor.

Suffixes are inherited. `DrinkCanBaseFull` has `suffix: Full`, so its children start as "Full"
unless they override it. `DrinkColaCanEmpty` sets `suffix: Empty` to replace it.

## 7.4 Non-entity prototypes use translation keys

Many other prototype kinds expect a **localization key** in `name`, not English text. This is
because those prototypes are not automatically localized from their ID the same way entities are.

### Reagent

Real example from `Resources/Prototypes/_PS/Reagents/oxidant.yml`:

```yaml
- type: reagent
  id: Oxidant
  name: reagent-name-oxidant
  group: Toxins
  desc: reagent-desc-oxidant
  physicalDesc: reagent-physical-desc-ferrous
  color: "#5c0000"
```

The English text lives in an `.ftl` file, for example
`Resources/Locale/en-US/_PS/reagents/oxidant.ftl`:

```ftl
reagent-name-oxidant = oxidant
reagent-desc-oxidant = High heat capacity, low viscosity, chemically inert, corrosion preventing, electrically insulated fluid. The stuff of synth life.
```

`ReagentPrototype` requires `name`, `desc`, and `physicalDesc`; all three are localization keys.

### Stack

Real example from `Resources/Prototypes/Stacks/engineering_stacks.yml`:

```yaml
- type: stack
  id: InflatableWall
  name: stack-inflatable-wall
  spawn: InflatableWallStack1
  maxCount: 10
```

`name` is a localization key. `spawn` is an entity ID. `maxCount` is the maximum number in one
stack.

### Tile

Real example from `Resources/Prototypes/_NF/Tiles/floors.yml`:

```yaml
- type: tile
  id: FloorBedrock
  name: tiles-bedrock-floor
  sprite: /Textures/_NF/Tiles/bedrock.png
  variants: 4
  placementVariants:
  - 1.0
  - 1.0
  - 1.0
  - 1.0
  baseTurf: Space
  isSubfloor: true
  footstepSounds:
    collection: FootstepAsteroid
  itemDrop: FloorTileItemBedrock
  heatCapacity: 10000
  weather: true
```

The repository convention is a translation key like `tiles-bedrock-floor`, with the English text in
a `.ftl` file.

### Fluent warnings

- Duplicate localization keys across all files in the same language are a **load error**.
- In Fluent (`en-US`), text after a value is **part of the value**, not a comment. A line like
  `reagent-name-x = water # the good stuff` translates to "water # the good stuff". Put comments on
  their own line starting with `#`.

---

# Part 8 — Spawn menu, categories, and maps

## 8.1 Abstract vs spawnable

- `abstract: true` means "template only, not a real object."
- A child of an abstract prototype is not automatically abstract. It is a real object unless it
  also sets `abstract: true`.
- `categories: [ HideSpawnMenu ]` means "real object, but keep it out of the spawn menu."

Use `abstract` for shared templates. Use `HideSpawnMenu` for real things that should not be spawned
by hand, such as bullets, effects, and internal body parts.

## 8.2 Categories

Categories are named groups used by the spawn menu, the map editor, and some tests. You put an
entity into a category with the `categories` field:

```yaml
  categories: [ DoNotMap ]
```

Real category definition from `Resources/Prototypes/Entities/categories.yml`:

```yaml
- type: entityCategory
  id: DoNotMap
  name: entity-category-name-donotmap
  suffix: entity-category-suffix-donotmap
```

Common categories:

| Category | Meaning |
|---|---|
| `DoNotMap` | Should not be placed on a map. A test checks maps for these |
| `HideSpawnMenu` | Real entity, but hidden from the spawn menu |
| `Mapping` | Useful for mappers |
| `Spawner` | Spawns other entities |
| `Debug` | Debug/testing entities |

Categories are usually inherited. `HideSpawnMenu` is deliberately **not** inherited, so a child of
a hidden prototype can still be visible if it is a real thing.

To make a new category, add an `entityCategory` prototype and a localization key. You rarely need
to do this; use an existing category if one fits.

## 8.3 Placement

`placement` controls how admins and mappers place the entity. It is mainly for machines and
structures.

Real example from `Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml`:

```yaml
  placement:
    mode: SnapgridCenter
```

Common modes:

| Mode | What it does |
|---|---|
| `SnapgridCenter` | Snaps the entity to the centre of a grid tile |
| `SnapgridCenterOffset` | Palmtree-specific: centre snapping plus an admin-configured offset |
| `AlignAtmosPipeLayers` | Aligns atmos pipes to the correct layer |
| `SnapgridBorder` | Snaps to tile borders |

For most items, you do not need a placement mode. If you are making a machine, copy the mode from a
similar machine.

## 8.4 Making it obtainable in normal play

**Creating a prototype does not put it in the world.** A new item is spawnable by admins, but
players cannot get it until you add it to a source. Common sources:

- a vending machine inventory,
- a cargo product,
- a loot table or salvage spawner,
- a construction graph or crafting recipe,
- a loadout entry,
- a map,
- a store or uplink catalog.

Real example from `Resources/Prototypes/Catalog/VendingMachines/Inventories/cola.yml`:

```yaml
- type: vendingMachineInventory
  id: RobustSoftdrinksInventory
  startingInventory:
    DrinkColaCan: 4
    DrinkGrapeCan: 2
    DrinkRootBeerCan: 2
    DrinkIcedTeaCan: 2
    DrinkSolDryCan: 2
    DrinkLemonLimeCan: 2
    DrinkLemonLimeCranberryCan: 2
    DrinkFourteenLokoCan: 2
  contrabandInventory:
    DrinkColaBottleFull: 2
    DrinkChangelingStingCan: 2
  emaggedInventory:
    DrinkNukieCan: 2
```

The keys under `startingInventory` are **entity IDs**. The numbers are how many the machine starts
with. `contrabandInventory` is extra stock unlocked when the machine's `Contraband` flag is enabled
(usually through access or hacking). `emaggedInventory` is the stock that appears after the machine
is emagged.

If you add your item here, the soda vendor will sell it. Ask before changing economy-related stock;
another server may be balancing those numbers.

---

# Part 9 — Other kinds of prototypes

Entities are the most common kind of prototype, but they are not the only kind. The same YAML
rules apply. The main differences are the `type`, the fields, and whether `name` is plain text or a
translation key.

## 9.1 Stack

A `stack` describes a stackable material. It is not the physical sheet; it is the rule for how
sheets stack.

Real example from `Resources/Prototypes/Stacks/engineering_stacks.yml`:

```yaml
- type: stack
  id: InflatableWall
  name: stack-inflatable-wall
  spawn: InflatableWallStack1
  maxCount: 10
```

| Field | Meaning |
|---|---|
| `id` | Unique stack ID |
| `name` | Localization key for the stack name |
| `spawn` | The entity ID that one unit spawns as |
| `maxCount` | Maximum number in one stack |

The entity that uses the stack has a `Stack` component that points to this ID. Search for
`Stack` in a sheet prototype to see how it is wired.

## 9.2 Reagent

A `reagent` is a chemical or liquid. It is not an item; it is what is inside a container.

Real example from `Resources/Prototypes/_PS/Reagents/oxidant.yml`:

```yaml
- type: reagent
  id: Oxidant
  name: reagent-name-oxidant
  group: Toxins
  desc: reagent-desc-oxidant
  physicalDesc: reagent-physical-desc-ferrous
  color: "#5c0000"
  worksOnTheDead: true
  metabolisms:
    Poison:
      effects:
      - !type:HealthChange
        damage:
          types:
            Poison: 3.5
```

| Field | Meaning |
|---|---|
| `id` | Unique reagent ID, used by `ReagentId` in solutions and reactions |
| `name` | Localization key for the name |
| `group` | Category such as `Toxins`, `Medicine`, `Drinks` |
| `desc` | Localization key for the examine text |
| `physicalDesc` | Localization key for how it looks/tastes/smells |
| `color` | Liquid colour, used by beakers and spills |
| `worksOnTheDead` | Whether it affects dead bodies |
| `metabolisms` | What happens when a living thing metabolizes it |

`!type:HealthChange` is a code-defined metabolism effect. It applies damage of the listed types.
This is advanced; copy a working reagent and change the numbers.

## 9.3 Reaction

A `reaction` is a recipe that turns reagents into other reagents. It is what makes chemistry work.

Real example from `Resources/Prototypes/_NF/Recipes/Reactions/drinks.yml`:

```yaml
- type: reaction
  id: Coffee
  minTemp: 353 # ~80 deg C
  reactants:
    Water:
      amount: 15
    CoffeeGrounds:
      amount: 5
  products:
    Coffee: 20
```

| Field | Meaning |
|---|---|
| `id` | Unique reaction ID |
| `minTemp` | Minimum temperature in Kelvin. 273 K is freezing, 373 K is boiling |
| `reactants` | Reagent IDs and amounts consumed |
| `products` | Reagent IDs and amounts produced |
| `requiredMixerCategories` | Optional; e.g. `Shake` requires a shaker |

Real example with a mixer requirement:

```yaml
- type: reaction
  id: Eggnog
  requiredMixerCategories:
  - Shake # close enough to meringue?
  reactants:
    Sugar:
      amount: 1
    Cream:
      amount: 1
    Egg:
      amount: 1
    Rum:
      amount: 1
  products:
    Eggnog: 4
```

Reactions do not have a `name` field. The game builds the reaction's identity from its products and
ID.

## 9.4 Decal

A `decal` is a picture painted on a floor or wall. It has no physics and is not an item.

Real example from `Resources/Prototypes/Decals/lettering.yml`:

```yaml
- type: decal
  id: StencilNumber0
  tags: ["station", "markings"]
  sprite:
    sprite: Decals/stencil.rsi
    state: stencil0
```

| Field | Meaning |
|---|---|
| `id` | Unique decal ID |
| `tags` | Which paint tools or categories show it |
| `sprite.sprite` | The RSI folder |
| `sprite.state` | The image inside the RSI |

Decals are placed by mappers and by the spray painter. The `tags` list controls where they appear in
those tools.

## 9.5 Tile

A `tile` is a floor or wall tile. It is a prototype kind of its own, not an entity.

Real example from `Resources/Prototypes/_NF/Tiles/floors.yml`:

```yaml
- type: tile
  id: FloorBedrock
  name: tiles-bedrock-floor
  sprite: /Textures/_NF/Tiles/bedrock.png
  variants: 4
  placementVariants:
  - 1.0
  - 1.0
  - 1.0
  - 1.0
  baseTurf: Space
  isSubfloor: true
  footstepSounds:
    collection: FootstepAsteroid
  itemDrop: FloorTileItemBedrock
  heatCapacity: 10000
  weather: true
```

| Field | Meaning |
|---|---|
| `id` | Unique tile ID |
| `name` | Localization key for the tile name |
| `sprite` | A direct PNG, or an RSI path. This one is a direct PNG |
| `variants` | How many visual variants exist |
| `placementVariants` | Weights for choosing a variant |
| `baseTurf` | What is underneath / what it can be placed on |
| `isSubfloor` | Whether it is a subfloor (space, plating) |
| `footstepSounds` | Sound collection played when walking on it |
| `itemDrop` | The entity dropped when the tile is pried up |
| `heatCapacity` | How much heat the tile absorbs |
| `weather` | Whether weather can affect it |

Tiles are for mappers. If you only want a new item, you do not need a tile.

## 9.6 Cargo product

A `cargoProduct` is one line in the cargo ordering console.

Real example from `Resources/Prototypes/Catalog/Cargo/cargo_service.yml`:

```yaml
- type: cargoProduct
  id: ServiceJanitorial
  icon:
    sprite: Objects/Specific/Janitorial/janitorial.rsi
    state: cleaner
  product: CrateServiceJanitorialSupplies
  cost: 2500
  category: cargoproduct-category-name-service
  group: market
```

| Field | Meaning |
|---|---|
| `id` | Unique cargo product ID |
| `icon` | The picture shown in the cargo console |
| `product` | The entity ID that is delivered |
| `cost` | The price in cargo currency |
| `category` | Localization key for the cargo tab |
| `group` | The cargo group, usually `market` |

The delivered `product` is usually a crate entity. That crate has its own contents, often defined
by a `StorageFill` component or a spawn table.

## 9.7 Vending machine inventory

A `vendingMachineInventory` defines what a vending machine sells.

Real example from `Resources/Prototypes/Catalog/VendingMachines/Inventories/cola.yml`:

```yaml
- type: vendingMachineInventory
  id: RobustSoftdrinksInventory
  startingInventory:
    DrinkColaCan: 4
    DrinkGrapeCan: 2
    DrinkRootBeerCan: 2
    DrinkIcedTeaCan: 2
  contrabandInventory:
    DrinkColaBottleFull: 2
  emaggedInventory:
    DrinkNukieCan: 2
```

- `startingInventory` — normal stock.
- `contrabandInventory` — extra stock unlocked when the machine's `Contraband` flag is enabled
  (usually through access or hacking).
- `emaggedInventory` — stock after the machine is emagged.

The keys are entity IDs; the values are counts. The machine entity itself points to this inventory
ID with a `VendingMachine` component.

## 9.8 Loadout and loadout group

A `loadout` is one choice in character creation. A `loadoutGroup` is a group of choices.

Real `loadout` from `Resources/Prototypes/_PS/Loadouts/Jobs/Contractor/kudzu_dress.yml`:

```yaml
- type: loadout
  id: ContractorKudzuDress
  effects:
  - !type:GroupLoadoutEffect
    proto: ContractorT1
  hideEffects:
  - !type:GroupLoadoutEffect
    proto: NFCoatCapable
  equipment:
    outerClothing: ClothingOuterKudzuDress
  groupBy: "OuterJackets"
```

| Field | Meaning |
|---|---|
| `id` | Unique loadout ID |
| `effects` | Requirements to unlock the choice |
| `hideEffects` | Requirements that hide the choice instead of showing it locked |
| `equipment` | Which body slot gets which entity ID |
| `groupBy` | A label that groups similar choices |

A loadout is **not reachable** until it is listed in a `loadoutGroup`. Real group from
`Resources/Prototypes/_NF/Loadouts/mail_carrier_loadout_groups.yml`:

```yaml
- type: loadoutGroup
  id: MailCarrierJumpsuit
  name: loadout-group-contractor-jumpsuit
  loadouts:
  - MailCarrierClothingUniformJumpsuitMailCarrier
  - MailCarrierClothingUniformJumpskirtMailCarrier
  - MailCarrierClothingUniformJumpsuitMilitaryMailCarrier
  fallbacks:
  - MailCarrierClothingUniformJumpsuitMailCarrier
  - MailCarrierClothingUniformJumpskirtMailCarrier
  subgroups:
  - CoyoteJumpsuit
```

- `loadouts` is the list of choices.
- `fallbacks` are the default choices applied when the player has not picked one.
- `subgroups` include other groups.
- `name` is a localization key.

For a full walkthrough, see `.ai/guides/adding-loadouts.md`.

## 9.9 Construction graph

A `constructionGraph` describes the steps to build something. The entity that can be built points
to the graph with a `Construction` component.

Real example from `Resources/Prototypes/_PS/Recipes/Crafting/Graphs/bots/roomba.yml`:

```yaml
- type: constructionGraph
  id: Roomba
  start: start
  graph:
  - node: start
    edges:
    - to: bot
      steps:
      - tag: Bucket
        icon:
          sprite: Objects/Tools/bucket.rsi
          state: icon
        name: construction-graph-tag-bucket
      - tag: ProximitySensor
        icon:
          sprite: Objects/Misc/proximity_sensor.rsi
          state: icon
        name: construction-graph-tag-proximity-sensor
        doAfter: 2
      - tag: BorgArm
        icon:
          sprite: Mobs/Silicon/drone.rsi
          state: l_hand
        name: construction-graph-tag-borg-arm
        doAfter: 2
      - tag: Mop
        icon:
          sprite: Objects/Specific/Janitorial/mop.rsi
          state: mop
        name: construction-graph-tag-mop
        doAfter: 2
  - node: bot
    entity: MobRoomba
```

| Field | Meaning |
|---|---|
| `start` | The node the construction starts at |
| `graph` | The list of nodes and edges |
| `node` | One stage of construction |
| `edges` | Ways to move from this node to another |
| `to` | The destination node |
| `steps` | Items or actions needed |
| `tag` | An item tag, not a specific item ID |
| `icon` | The picture shown in the construction menu |
| `name` | Localization key for the step |
| `doAfter` | Seconds the step takes |
| `entity` | The entity spawned at the final node |

The roomba entity has a matching component:

```yaml
  - type: Construction
    graph: Roomba
    node: bot
```

`graph` points to this construction graph. `node` is the node the entity represents when it is
finished.

## 9.10 Sound collection

A `soundCollection` is a list of sounds that can be picked at random.

Real example from `Resources/Prototypes/_PS/SoundCollections/weapons.yml`:

```yaml
- type: soundCollection
  id: Phaser
  files:
  - /Audio/_PS/Weapons/Phaser/phaser1.ogg
  - /Audio/_PS/Weapons/Phaser/phaser2.ogg
```

- `id` is what `collection:` points to.
- `files` is a list of paths. A path starting with `/` is relative to `Resources/`.
- A collection can have one file or many.

## 9.11 Entity category

An `entityCategory` groups entities in the spawn menu and mapping tools.

Real example from `Resources/Prototypes/Entities/categories.yml`:

```yaml
- type: entityCategory
  id: DoNotMap
  name: entity-category-name-donotmap
  suffix: entity-category-suffix-donotmap
```

| Field | Meaning |
|---|---|
| `id` | Category ID used in `categories:` |
| `name` | Localization key for the category name |
| `description` | Optional localization key for a tooltip |
| `suffix` | Optional default suffix added to all entities in the category |
| `hideSpawnMenu` | If true, all entities in this category are hidden from the spawn menu |
| `components` | If set, entities with these components are added automatically |
| `inheritable` | If true, children of a member also belong to the category |

You usually do not need a new category. Use `DoNotMap` or `HideSpawnMenu` when they fit.

## 9.12 Other kinds you may see

The repository contains many more prototype kinds. You do not need to master them all. Here is a
quick list:

| Kind | What it describes | Where to look |
|---|---|---|
| `damageType` | A damage category such as Blunt or Heat | `Resources/Prototypes/Damage/types.yml` |
| `latheRecipe` | A recipe for a fabricator | `Resources/Prototypes/Recipes/Lathes/` |
| `technology` | A research node | `Resources/Prototypes/Research/` |
| `guideEntry` | A guidebook page | `Resources/Prototypes/Guidebook/` |
| `gameMap` | A map | `Resources/Prototypes/Maps/` |
| `gamePreset` | A round type | `Resources/Prototypes/game_presets.yml` |
| `entityTable` | A weighted random loot table | `Resources/Prototypes/EntityLists/` |
| `mailDeliveryPool` | A pool of mail deliveries | `Resources/Prototypes/_NF/Mail/` |

If you need one of these, copy an existing example and follow the same pattern. The same YAML rules
apply.

---

# Part 10 — A full walkthrough: a new soda

Let's put everything together and make a new drink: **Palmtree Cola**. We will use only existing
assets and existing components, so it will work without any new art or code.

## Step 1: Pick a model

We want a full drink can. The closest existing thing is `DrinkColaCan`:

```yaml
- type: entity
  parent: DrinkCanBaseFull
  id: DrinkColaCan
  name: Space Cola can
  description: A sweet, carbonated soft drink.
  components:
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
        reagents:
        - ReagentId: Cola
          Quantity: 30
      grindable: *grindable
  - type: Tag
    tags:
    - Cola
    - DrinkCan
  - type: Sprite
    sprite: Objects/Consumable/Drinks/cola.rsi
  - type: Item
    sprite: Objects/Consumable/Drinks/cola.rsi
```

This tells us:

- the parent is `DrinkCanBaseFull`;
- the solution is named `drink`;
- the reagent is `Cola`;
- `grindable: *grindable` is the recycling solution, using the anchor shortcut from the same file;
- the sprite folder is `Objects/Consumable/Drinks/cola.rsi`;
- the in-hand sprite is the same folder.

## Step 2: Create the file

Create:

```text
Resources/Prototypes/_PS/Entities/Objects/Consumable/Drinks/palmtree_cola.yml
```

The folder may not exist yet. Create it. The file name is for humans; the ID inside is what
matters.

## Step 3: Write the header and the differences

Copy the idea, but use a new ID and your own name/description. This is the complete file:

```yaml
# Palmtree: a house cola for the bar menu.
# Uses the existing cola sprite and the existing Cola reagent, so it needs no new art or code.
- type: entity
  parent: DrinkCanBaseFull
  id: PSPalmtreeColaCan
  name: Palmtree cola can
  description: A sweet, carbonated soft drink with a hint of palm fruit. The can is painted in bright island colours.
  components:
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
        reagents:
        - ReagentId: Cola
          Quantity: 30
      grindable:
        reagents:
        - ReagentId: Aluminium
          Quantity: 4
        - ReagentId: Iron
          Quantity: 1
  - type: Tag
    tags:
    - Cola
    - DrinkCan
  - type: Sprite
    sprite: Objects/Consumable/Drinks/cola.rsi
  - type: Item
    sprite: Objects/Consumable/Drinks/cola.rsi
```

Why this is safe:

- `DrinkCanBaseFull` already provides the can shape, opening, trash behaviour, tags, and physics.
- `SolutionContainerManager` sets the drink to 30 units of `Cola` and copies the parent's
  `grindable` recipe (aluminium and iron) so recycling still works.
- `Tag` marks it as a cola and a drink can.
- `Sprite` and `Item` point to an existing RSI folder.
- The ID `PSPalmtreeColaCan` is unique.
- The name and description are plain English.
- The file is under `_PS`, so upstream updates will not overwrite it.

## Step 4: Test it

Save the file. Restart your development server and client. Then:

1. If you have admin permissions, press **F7** → **Adminbus** → **Spawn Entities**.
2. Search for `palmtree` or `PSPalmtreeColaCan`.
3. Spawn it.
4. Examine it, pick it up, and drink it.

Or use the console:

```text
spawn PSPalmtreeColaCan
```

## Step 5: Check the behaviour

Verify:

- The item appears under the name "Palmtree cola can".
- The description is correct.
- The can uses the cola sprite.
- You can pick it up and put it in a backpack.
- You can open and drink it.
- It becomes trash when empty (inherited from the parent).
- It can be ground up for materials (using the `grindable` block copied from the parent).

## Step 6: Make a variant (optional)

If you want an empty version, copy the real empty-can pattern:

```yaml
- type: entity
  parent: [ DrinkBaseOpenableOpen, PSPalmtreeColaCan ]
  id: PSPalmtreeColaCanEmpty
  suffix: Empty
  components:
  - type: SolutionContainerManager
    solutions:
      drink:
        maxVol: 30
      grindable:
        reagents:
        - ReagentId: Aluminium
          Quantity: 4
        - ReagentId: Iron
          Quantity: 1
  - type: Tag
    tags:
    - Cola
    - DrinkCan
    - Trash
```

This combines the "already open" parent with your full can, starts the drink solution empty, and
adds the `Trash` tag. Notice that the `grindable` block is copied again: re-declaring `solutions`
replaces the whole block, so any solution you still want must be listed.

## Step 7: Make it obtainable

If you want players to buy it from a soda machine, add it to the machine's inventory:

```yaml
- type: vendingMachineInventory
  id: RobustSoftdrinksInventory
  startingInventory:
    PSPalmtreeColaCan: 2
```

Do this only after the item is tested and balanced. Changing a vendor affects the economy and the
lobby/station supply, so ask the maintainer first.

## Step 8: Run the linter

Ask a coder to run the YAML linter, or run it yourself if you have the development environment
set up. The exact commands are in [Part 11](#part-11--testing-and-validation).

---

# Part 11 — Testing and validation

## 11.1 Quick in-game test

Every prototype change should be tested in-game. The fastest loop is:

1. Edit the `.yml` file.
2. Restart the server and client.
3. Use the admin spawn menu or `spawn <ID>`.
4. Check the item's appearance, name, description, and behaviour.
5. Fix one problem at a time.

If you are only changing a sprite or description, you still need to restart to see the change.
Prototypes are loaded at startup, not every time you open a menu.

## 11.2 What to check

Use this checklist for a new item:

- [ ] The prototype appears in the spawn menu.
- [ ] The name is correct.
- [ ] The description is correct.
- [ ] The sprite is visible and the right size.
- [ ] The item can be picked up and dropped.
- [ ] The item can be stored in a backpack or box.
- [ ] The item does what it is supposed to do when used.
- [ ] The item has the correct sounds.
- [ ] The item can be destroyed or deconstructed if it is a machine.
- [ ] The item is not marked `DoNotMap` if it is meant to be mapped.
- [ ] The item is obtainable in normal play, if that is the goal.

## 11.3 The YAML linter

The repository has a tool called **Content.YAMLLinter**. It reads every prototype and checks for
mistakes: unknown components, unknown fields, duplicate IDs, missing prototype references, bad
localization, and more.

The commands, from the repository root:

```bash
dotnet build SpaceStation14.sln
dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj
dotnet run --project Content.YAMLLinter -c Debug --no-build
```

If it succeeds, it prints something like:

```text
No errors found in 12345 ms.
```

If it fails, it prints lines with the file, line, and column:

```text
::error in Resources/Prototypes/_PS/Entities/Objects/Consumable/Drinks/palmtree_cola.yml(12,5)  ...
```

Read the reason at the end of the line. Common reasons:

| Message or symptom | Likely cause |
|---|---|
| `Unknown component type` | A typo or a component that does not exist |
| `Field "..." not found` | A typo in a setting, or wrong indentation |
| `Duplicate prototype ID` | Two prototypes of the same kind have the same ID |
| `Invalid prototype ID` | A `parent`, reagent, product, or other reference does not exist |
| Missing RSI / sprite state | The sprite path or `state` does not exist in the RSI |
| `Expected ...` | Wrong kind of value: text where a number should be, or a list where a single value should be |

> **Rebuild the linter after C# changes.** If a programmer changed the code, rebuild
> `Content.YAMLLinter` before running it. Otherwise it may use an old copy of the game and report
> confusing errors. See `.ai/systems/content-pipeline-and-tooling.md`.

### Why the file name may be wrong

When inheritance is involved, the reported **file name** can be misleading. Trust the line number
and the reason, and check both the child you edited and the parents it inherits from. If the file
name points at a template you never touched, the real mistake is probably in a child that uses it.

## 11.4 The bigger test suite

For a content-only change, the linter is usually enough. For changes that touch code, species,
loadouts, markings, or maps, the project also has:

```bash
dotnet test Content.Tests/Content.Tests.csproj
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj
```

The integration tests load the whole game, so they are slower. A coder can run the relevant subset.
For example, entity spawning and prototype tests:

```bash
dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj \
  --filter "FullyQualifiedName~EntityTest"
```

Do not be afraid to ask for help with these commands. They are developer tools, not player tools.

## 11.5 Reloading without a full restart

A normal server reads prototypes at startup. Some development builds can hot-reload prototype
files, but that is a developer convenience, not something to rely on. If your change does not show
up:

1. Save the file.
2. Restart the server.
3. Restart the client.
4. Rejoin.

If it still does not show up, check that you edited the file inside `Resources/Prototypes/` and not
a copy somewhere else.

---

# Part 12 — Troubleshooting

## 12.1 The item is not in the spawn menu

Check, in this order:

1. **Is it abstract?** `abstract: true` hides it. Remove it or make a non-abstract child.
2. **Is it in `HideSpawnMenu`?** `categories: [ HideSpawnMenu ]` hides it. Remove the category.
3. **Is the file in the right place?** It must be under `Resources/Prototypes/` and end in `.yml`.
4. **Did the file load?** Check the server console for YAML errors.
5. **Is the ID unique?** A duplicate ID can prevent the prototype from loading.
6. **Did you restart?** Prototypes are read at startup.
7. **Are you searching for the right thing?** The spawn menu searches the name, ID, and suffix.
8. **Was it intentionally disabled?** Some upstream prototypes are made abstract by
   `Resources/IgnoredPrototypes/ignoredPrototypes.yml`. Do not remove entries from that file to
   force something back without asking; the fork disabled it for a reason.

## 12.2 It spawns but is invisible

Usually a sprite problem.

Check:

- The `sprite:` path points to an existing `.rsi` folder under `Resources/Textures/`.
- The `state:` name exists in that folder's `meta.json`.
- You did not misspell the folder or file name. Paths are case-sensitive.
- The RSI has a `meta.json` with the state listed.
- The sprite is not fully transparent or the wrong size.
- For a layered sprite, the layers have valid states and are not all `visible: false`.

A quick test: spawn a similar existing item and compare its `Sprite` block with yours.

## 12.3 It has no name, or shows a weird key

Possible causes:

- The prototype has no `name`, and its parent has no name either.
- The name is a localization key, but there is no `.ftl` entry for it. Entity names should be plain
  text; non-entity names may be keys and need an entry.
- You edited the wrong prototype (two prototypes with similar IDs).
- The client is using a cached localization file; restart the client.

## 12.4 It can't be picked up

The entity needs an `Item` component, usually from `BaseItem` or another item parent.

Check:

- Does the `parent` chain include `BaseItem` or a parent that has `Item`?
- Did you accidentally set `abstract: true` on the final prototype?
- Is the entity anchored or fixed in place by a `Transform` or `Physics` setting?
- Is it actually a structure parent (`BaseStructure`) rather than an item parent?

## 12.5 It doesn't do anything when used

A prototype only does what its components do.

Examples:

- A drink needs the drinking components from `DrinkBase` or `DrinkCanBaseFull`.
- A weapon needs `MeleeWeapon` or a gun component.
- A machine needs its machine system component, power, and wiring.
- A light needs `PoweredLight` and `PointLight`.

If the behaviour is missing, the parent probably does not have it, and you need to add the
component (if it exists) or choose a different parent.

## 12.6 Unknown component or unknown field

The linter and the server console will name the problem.

Common causes:

- **Case:** `sprite` is not `Sprite`. `Item` is not `item`.
- **Typo:** `SolutionContainerManager` is easy to misspell.
- **Wrong nesting:** a field is indented under the wrong component.
- **Invented component:** the game does not have a component with that name. Ask a coder.
- **Server/client difference:** a component may exist only on one side. The linter reports these
  carefully; read the full message.

## 12.7 Duplicate ID

Two prototypes of the same kind cannot share an ID. Search the repository for your ID:

```bash
grep -rn "id: YourID" Resources/Prototypes
```

If it already exists, choose a new ID. Do not rename the existing one unless you know every file
that references it.

## 12.8 It works but players can't get it

A prototype is not a source. See [Part 8.4](#84-making-it-obtainable-in-normal-play). Add it to a
vendor, cargo product, loot table, construction graph, loadout, map, or store.

## 12.9 Quick diagnostic table

| Symptom | Most likely cause | First fix |
|---|---|---|
| Not in spawn menu | Abstract or `HideSpawnMenu` | Remove the flag/category |
| White square or missing sprite | Bad RSI path or state | Check `Resources/Textures` and `meta.json` |
| Name shows as a key | Loc key without an `.ftl` entry | Use plain text for entities, or add the `.ftl` entry |
| Can't pick it up | No `Item` component | Use an item parent or add `Item` |
| No description | No `description`, and parent has none | Add a description |
| Falls through the floor | No physics/fixtures | Use an item or structure parent |
| Can't be placed on a map | `DoNotMap` category | Remove the category if it should be mapped |
| Server says duplicate ID | Same ID already exists | Choose a unique ID |
| Server says unknown component | Typo/case/invented component | Copy the exact name from a working prototype |
| Server says field not found | Typo or wrong indentation | Check the line above the reported line |
| Changes don't show | Server/client not restarted | Restart both |
| Works for admin, not players | Not added to a source | Add it to a vendor/cargo/loot/recipe/map |

---

# Part 13 — House rules and best practices

These rules keep the repository mergeable and the game stable.

## 13.1 Folder rules

- New Palmtree content goes under `Resources/Prototypes/_PS/...`.
- New Palmtree sprites go under `Resources/Textures/_PS/...`.
- New Palmtree audio goes under `Resources/Audio/_PS/...`.
- Never add new files to upstream folders (`Resources/Prototypes/Entities`, `Resources/Textures/Objects`,
  etc.) for Palmtree-only content.
- Ported content keeps its source prefix (`_NF`, `_CS`, `_Floof`, `_DV`, ...). Do not rename it to
  `_PS`; the prefix records where it came from.
- Do not edit `RobustToolbox/`. That is the engine and it is not ours to change.

## 13.2 ID rules

- Use a unique ID. Two entity prototypes cannot share one.
- Start new Palmtree IDs with `PS` when possible. It makes conflicts with upstream less likely.
- Do not rename an existing ID unless you have searched for every reference. Vendors, maps,
  loadouts, construction graphs, and code can all point at an ID.
- Keep IDs stable after release. Renaming is a breaking change.

## 13.3 Editing rules

- Copy a working prototype; do not start from a blank file unless you are confident.
- Change **one thing at a time** and test. If you change five things and it breaks, you will not know
  which one caused it.
- Do not delete fields you do not understand. They may be required by the parent or the system.
- Keep comments useful. Prefix Palmtree-specific comments with `# Palmtree:` so they are easy to
  find.
- Match the surrounding style: lowercase file names, CamelCase IDs, two-space indentation.
- Do not leave commented-out experiments in a file you are handing over. Remove them or explain
  them.

## 13.4 Testing rules

- Test on a development server, not the live server.
- Spawn the item, use it, and check the description.
- Test the failure cases too: what happens when it is empty, broken, unpowered, or used on the
  wrong thing?
- Run the YAML linter before asking for a review.
- If you changed a map, load the map and walk around it.

## 13.5 Asset rules

- Keep `meta.json` license and copyright information.
- Do not generate new art unless the maintainer has approved it.
- Do not copy art from a source that does not allow it. Keep the license that came with the art.
- Use `.ogg` for audio and `.png` for sprites.
- Put assets in the matching `_PS` folder.

## 13.6 Before you ask for a review

Use this checklist:

- [ ] The file is in the right `_PS` folder.
- [ ] The ID is unique and starts with `PS` (if new).
- [ ] The name and description are plain English (for entities).
- [ ] Every sprite path and state exists.
- [ ] Every parent ID exists.
- [ ] Every reagent/product/entity reference exists.
- [ ] The item was spawned and used in-game.
- [ ] The YAML linter passes.
- [ ] The change is one logical thing, not five unrelated things.
- [ ] Comments explain anything unusual.
- [ ] You did not edit upstream files for Palmtree-only content.

---

# Part 14 — Glossary

| Term | Meaning |
|---|---|
| **Prototype** | A recipe card in a `.yml` file |
| **Server** | The computer that runs the game world and decides what is true |
| **Client** | The game program on a player's computer |
| **Kind** | The family a prototype belongs to, written in its `type:` line (entity, reagent, stack, ...) |
| **Entity** | A physical thing in the world, made from an entity prototype |
| **Component** | A block of behaviour attached to an entity |
| **System** | The C# code that makes components do things |
| **ID** | The unique, invisible name of a prototype |
| **Parent** | A prototype you inherit settings from |
| **Inheritance** | Copying a parent's settings into a child, then applying the child's changes |
| **Abstract** | A template prototype that is not meant to be spawned |
| **Override** | Listing a component again in a child to change one or more of its settings |
| **YAML** | The text format used for prototypes |
| **RSI** | A folder of sprites with a `meta.json` |
| **State** | The name of one image inside an RSI |
| **Layer** | One image in a multi-image sprite |
| **Draw depth** | The order in which sprites are drawn relative to each other |
| **Tag** | A label used by other systems to find entities |
| **Enum** | A fixed list of allowed values, usually written like `LethalOnly` |
| **`!type:`** | A special YAML tag that selects a code-defined class |
| **Loc key** | A translation key used in `.ftl` files |
| **FTL / Fluent** | The localization file format used by the game |
| **Linter** | A tool that checks prototypes for mistakes |
| **Spawn menu** | The admin menu for creating entities |
| **Map editor** | The tool mappers use to place entities and tiles |
| **Reagent** | A chemical or liquid inside a solution |
| **Solution** | A named container of reagents, such as `drink` |
| **Stack** | A stackable material rule |
| **Reaction** | A chemistry recipe that turns reagents into other reagents |
| **Decal** | A picture painted on a floor or wall |
| **Tile** | A floor or wall tile |
| **Loadout** | A character-creation gear choice |
| **Construction graph** | A crafting recipe with steps and nodes |
| **Sound collection** | A list of sounds picked at random |

---

# Part 15 — Copy-paste templates

These templates are starting points. Replace the IDs, names, paths, and numbers. Anything that looks
like `PSMyThing` or is written in `<angle brackets>` is a placeholder you must replace.

## 15.1 Entity template

```yaml
# <Short description of what this is and why it exists.>
- type: entity
  parent: BaseItem
  id: PSMyThing
  name: my thing
  description: A thing I made.
  components:
  - type: Sprite
    sprite: _PS/Objects/MyThing.rsi
    state: icon
  - type: Item
    size: Small
    sprite: _PS/Objects/MyThing.rsi
```

Use this only if `_PS/Objects/MyThing.rsi` actually exists. If you do not have art yet, copy an
existing item's sprite path temporarily, or ask for art.

## 15.2 Child variant template

```yaml
- type: entity
  parent: PSMyThing
  id: PSMyThingVariant
  suffix: Variant
  description: A variant of my thing.
  components:
  - type: Item
    size: Normal
```

Only the differences go in the child. Everything else is inherited.

## 15.3 Reagent template

```yaml
- type: reagent
  id: PSMyReagent
  name: reagent-name-ps-my-reagent
  group: Toxins
  desc: reagent-desc-ps-my-reagent
  physicalDesc: reagent-physical-desc-ps-my-reagent
  color: "#8a2be2"
```

Add the matching `.ftl` entries:

```ftl
reagent-name-ps-my-reagent = my reagent
reagent-desc-ps-my-reagent = A reagent I made.
reagent-physical-desc-ps-my-reagent = a strange purple liquid
```

## 15.4 Reaction template

```yaml
- type: reaction
  id: PSMyReaction
  minTemp: 300
  reactants:
    Water:
      amount: 1
    PSMyReagent:
      amount: 1
  products:
    PSMyProductReagent: 2
```

Replace the reagent IDs with real ones. Check the amounts and temperature in a beaker.

## 15.5 Stack template

```yaml
- type: stack
  id: PSMyStack
  name: stack-ps-my-stack
  spawn: PSMyStack1
  maxCount: 10
```

`PSMyStack1` must be an existing entity prototype for a single unit.

## 15.6 Decal template

```yaml
- type: decal
  id: PSMyDecal
  tags: ["station", "markings"]
  sprite:
    sprite: _PS/Decals/my_decal.rsi
    state: icon
```

## 15.7 Tile template

```yaml
- type: tile
  id: PSMyTile
  name: tiles-ps-my-tile
  sprite: /Textures/_PS/Tiles/my_tile.png
  variants: 1
  baseTurf: Plating
  isSubfloor: false
  footstepSounds:
    collection: FootstepTile
  itemDrop: FloorTileItemSteel
  heatCapacity: 10000
```

## 15.8 Cargo product template

```yaml
- type: cargoProduct
  id: PSMyCargoProduct
  icon:
    sprite: _PS/Objects/MyThing.rsi
    state: icon
  product: PSMyCrate
  cost: 1000
  category: cargoproduct-category-name-service
  group: market
```

## 15.9 Sound collection template

```yaml
- type: soundCollection
  id: PSMySounds
  files:
  - /Audio/_PS/MySounds/sound1.ogg
  - /Audio/_PS/MySounds/sound2.ogg
```

## 15.10 Pre-save checklist

Before saving a prototype file:

- [ ] The file ends in `.yml`.
- [ ] The file is under `Resources/Prototypes/_PS/`.
- [ ] The ID is unique.
- [ ] The parent exists.
- [ ] Indentation uses spaces, not tabs.
- [ ] Every `- type:` has a matching component name.
- [ ] Every list item is indented correctly.
- [ ] Every sprite path and state exists.
- [ ] Every referenced ID exists.
- [ ] The description is plain English (for entities).
- [ ] There are no duplicate settings in one prototype.

## 15.11 Commands and paths cheat sheet

| What | Where |
|---|---|
| Prototypes | `Resources/Prototypes/` |
| New prototypes | `Resources/Prototypes/_PS/` |
| Sprites | `Resources/Textures/` |
| New sprites | `Resources/Textures/_PS/` |
| Audio | `Resources/Audio/` |
| New audio | `Resources/Audio/_PS/` |
| Locale | `Resources/Locale/en-US/` |
| Spawn menu | F7 → Adminbus → Spawn Entities |
| Console | Tilde/backtick key (`` ` ``) |
| Spawn command | `spawn <PrototypeID>` |
| Linter | `dotnet run --project Content.YAMLLinter -c Debug --no-build` |
| Linter build | `dotnet build Content.YAMLLinter/Content.YAMLLinter.csproj` |
| Full build | `dotnet build SpaceStation14.sln` |
| Integration tests | `dotnet test Content.IntegrationTests/Content.IntegrationTests.csproj` |

---

# Part 16 — Real examples used in this guide

Use these files as references. They are working, existing prototypes.

| Topic | File |
|---|---|
| Simple drink item | `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_cans.yml` |
| Drink base | `Resources/Prototypes/Entities/Objects/Consumable/Drinks/drinks_base.yml` |
| Melee weapon with lots of components | `Resources/Prototypes/Entities/Objects/Weapons/Melee/baseball_bat.yml` |
| Generic item base | `Resources/Prototypes/Entities/Objects/base_item.yml` |
| Powered machine with wires and destruction | `Resources/Prototypes/_PS/Entities/Structures/Machines/bluespace_miner.yml` |
| Machine with visual states and fixtures | `Resources/Prototypes/_PS/Entities/Structures/Machines/weapon_detector.yml` |
| NPC with container and AI | `Resources/Prototypes/_PS/Entities/Mobs/NPCs/roomba.yml` |
| Light variants | `Resources/Prototypes/_PS/Entities/Structures/Lighting/colored_bulb_lights.yml` |
| Stack | `Resources/Prototypes/Stacks/engineering_stacks.yml` |
| Reagent | `Resources/Prototypes/_PS/Reagents/oxidant.yml` |
| Reaction | `Resources/Prototypes/_NF/Recipes/Reactions/drinks.yml` |
| Decal | `Resources/Prototypes/Decals/lettering.yml` |
| Tile | `Resources/Prototypes/_NF/Tiles/floors.yml` |
| Cargo product | `Resources/Prototypes/Catalog/Cargo/cargo_service.yml` |
| Vending machine stock | `Resources/Prototypes/Catalog/VendingMachines/Inventories/cola.yml` |
| Loadout | `Resources/Prototypes/_PS/Loadouts/Jobs/Contractor/kudzu_dress.yml` |
| Loadout group | `Resources/Prototypes/_NF/Loadouts/mail_carrier_loadout_groups.yml` |
| Construction graph | `Resources/Prototypes/_PS/Recipes/Crafting/Graphs/bots/roomba.yml` |
| Sound collection | `Resources/Prototypes/_PS/SoundCollections/weapons.yml` |
| Entity category | `Resources/Prototypes/Entities/categories.yml` |
| Entity localization override | `Resources/Locale/en-US/_NF/prototypes/entities/structures/machines/computers/computers.ftl` |
| Sprite metadata | `Resources/Textures/Objects/Consumable/Drinks/ice_tea_can.rsi/meta.json` |
| Machine sprite metadata | `Resources/Textures/_PS/Structures/Machines/weapon_detector.rsi/meta.json` |

## Related guides

- `.ai/guides/adding-loadouts.md` — loadouts and their groups.
- `.ai/guides/adding-markings.md` — markings and species appearance.
- `.ai/guides/adding-species.md` — species, body parts, and layers.
- `.ai/guides/adding-hotel-rooms.md` — room maps and archetype prototypes.
- `.ai/guides/adding-dungeons.md` — dungeon room packs and themes.
- `.ai/guides/porting-from-coyote.md` — porting content from Coyote/Floof.
- `.ai/guides/porting-from-ss13.md` — porting BYOND/SS13 content and maps.
- `.ai/systems/content-pipeline-and-tooling.md` — how prototypes, locale, maps, and tests fit together.
- `.ai/HAZARDS.md` — known traps, especially around localization, maps, and inherited data.

## Final word

Prototypes look like code, but they are really a set of recipes. Start from something that works,
change one thing, test it, and repeat. The most common beginner mistakes are indentation, a
misspelled component name, and forgetting to restart the server. If you can check those three
things, you can fix most problems yourself.

When in doubt, copy a working example and change less than you think you need to.
