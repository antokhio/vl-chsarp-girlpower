# vl-csharp-girlpower

Place to store some vvvv/vl PoCs and WiPs mostly for C# integration.

## Contents

* **[vl-stride-csharp-shader](./vl-stride-csharp-shader/)**: A Proof of Concept showing how to run and manage a custom shader from C# using `VL.Stride.Runtime`. By using the `ProcessNode` attribute, this bypasses the default VL patch compilation order, allowing the use of custom structs as shader inputs and enabling dynamic runtime shader compilation from chunks (mixins).