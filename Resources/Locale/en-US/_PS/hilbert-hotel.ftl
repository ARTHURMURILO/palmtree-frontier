# Hilbert's Hotel - check-in terminal, room controller and room prototypes.

## Room archetype names (referenced by hilbertHotelRoom prototypes).
hilbert-hotel-room-cozy = Cozy Room
hilbert-hotel-room-hotel-suite = Hotel Suite
hilbert-hotel-room-poolside = Poolside Suite
hilbert-hotel-room-cabin = Cabin in the Woods
hilbert-hotel-room-beach = Beach Condo
hilbert-hotel-room-library = Library
hilbert-hotel-room-apartment = Apartment
hilbert-hotel-room-debug = Debug Station (Testing)

## Shared room status names.
hilbert-hotel-status-open = Open
hilbert-hotel-status-guests = Guests Only
hilbert-hotel-status-locked = Locked

## Check-in terminal window.
hilbert-hotel-teleporter-title = Hilbert's Hotel
hilbert-hotel-teleporter-description = Every room is a private pocket of bluespace. Enter a room code to create a new room or join one someone shared with you.
hilbert-hotel-teleporter-code-label = Room code:
hilbert-hotel-teleporter-code-placeholder = e.g. 1337
hilbert-hotel-teleporter-random = Random
hilbert-hotel-teleporter-check-in = Check In
hilbert-hotel-teleporter-room-count = Rooms in use: { $count } / { $max }
hilbert-hotel-teleporter-templates-label = Room style (used only when creating a new room)
hilbert-hotel-teleporter-rooms-label = Public rooms
hilbert-hotel-teleporter-refresh = Refresh
hilbert-hotel-teleporter-template-selected = ▶ { $name }
hilbert-hotel-teleporter-join = Join
hilbert-hotel-teleporter-delete = Delete
hilbert-hotel-teleporter-delete-confirm = Really delete?
hilbert-hotel-teleporter-no-rooms = No public rooms right now.
hilbert-hotel-teleporter-room-entry = #{ $code } - { $name } - { $status } - owner: { $owner } - { $count } inside

## Room controller window.
hilbert-hotel-controller-title = Hilbert's Hotel Room Controller
hilbert-hotel-controller-code = Room code: { $code }
hilbert-hotel-controller-owner = Owner: { $name }
hilbert-hotel-controller-not-owner = Only the room's owner may change these settings.
hilbert-hotel-controller-visible = List this room publicly
hilbert-hotel-controller-status-button = Privacy: { $status } (click to change)
hilbert-hotel-controller-guests-label = Trusted guests
hilbert-hotel-controller-clear-guests = Clear trusted guests
hilbert-hotel-controller-occupants-label = Currently inside
hilbert-hotel-controller-no-guests = No trusted guests.
hilbert-hotel-controller-remove = Remove
hilbert-hotel-controller-no-occupants = Nobody else is inside.
hilbert-hotel-controller-occupant-owner = { $name } (owner)
hilbert-hotel-controller-occupant-trusted = { $name } (trusted)
hilbert-hotel-controller-trust = Trust
hilbert-hotel-controller-transfer = Make owner
hilbert-hotel-controller-delete = Delete room
hilbert-hotel-controller-delete-confirm = Really delete?

## Popups.
hilbert-hotel-popup-welcome = The bluespace folds around you. Welcome to room { $code }.
hilbert-hotel-popup-left = You step back out of the hotel room.
hilbert-hotel-popup-room-deleted = The room dissolves around you and you are sent back.

## Errors.
hilbert-hotel-error-too-far = You need to be standing at the terminal.
hilbert-hotel-error-incapacitated = You are in no state to check in.
hilbert-hotel-error-invalid-code = That is not a valid room code.
hilbert-hotel-error-locked = That room is locked.
hilbert-hotel-error-guests-only = That room only admits the owner's trusted guests.
hilbert-hotel-error-full = This terminal cannot support any more rooms right now.
hilbert-hotel-error-cooldown = Please wait a moment before creating another room.
hilbert-hotel-error-no-templates = No room styles are configured.
hilbert-hotel-error-load-failed = The room failed to materialize. Contact the concierge.
hilbert-hotel-error-not-owner = Only the room's owner may do that.
hilbert-hotel-error-guest-limit = This room's guest list is full.
hilbert-hotel-error-already-owns = You already own room { $code }. Delete it before creating another.
hilbert-hotel-error-occupied = Someone (or something) is still inside the room.
