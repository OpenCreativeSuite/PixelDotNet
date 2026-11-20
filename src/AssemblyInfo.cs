using System.Runtime.CompilerServices;

// The component suite tests a few internal wiring helpers (the viewport keyboard
// map, the pan gesture and the small markup formatters) that intentionally stay
// off the public API surface.
[assembly: InternalsVisibleTo("PixelDotNet.Tests")]
