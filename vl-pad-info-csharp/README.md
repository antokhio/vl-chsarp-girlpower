# VL.PadInfo (PoC)

A proof of concept for reading — and writing — VL **pad** metadata at runtime, without any access to the `.vl` XML.

## Why

Inside a running vvvv/VL app all we have is an `IVLObject` and its `IVLTypeInfo`. This PoC shows that the
information an author put on a pad in the patch (default, min, max, order, widget, labels, tags, custom
metadata like `isTest: true`) is still reachable at runtime through `IVLPropertyInfo` and the
`VL.Core.EditorAttributes` attributes.

## What it does

- `PadInspector.GetPads(instance)` — metadata for every pad of an object.
- `PadInspector.GetPad(instance, name)` — metadata for a single pad.
- `PadInspector.InspectPads(instance)` — human readable dump, handy for debugging.
- `PadInspector.SetPadValue(instance, name, value)` — write a value back into a pad.
- `PadInspector.TrySetPadValue(...)` / `TryResetPadToDefault(...)` — non-throwing variants.
- `PadInfo.SetValue(instance, value)` — write back straight from the pad info.

Each pad is described by a `PadInfo` record: `Name`, `Type`, `Value`, `Default`, `Min`, `Max`, `Order`,
`Widget`, `Label`, `Description`, `IsReadOnly`, `Tags`, `CustomMetaData` and the underlying `Property`.

## Notes

- Writing goes through `IVLPropertyInfo.WithValue`. For a VL **class** the same instance comes back
  (it was mutated); for a VL **record** a *new* instance is returned — always use the returned value.
- Values are coerced to the pad's CLR type before writing (enum names from strings, `IConvertible`
  via invariant culture).

## Build

Targets `net8.0`, references `VL.Core`, output goes to `..\lib\`.
