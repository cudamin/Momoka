namespace Windows.Graphics;

// Primitive endpoint rectangle only. No clipping, splitting, scaling or frame logic lives here.
internal readonly record struct RectInt32(int X, int Y, int Width, int Height);
