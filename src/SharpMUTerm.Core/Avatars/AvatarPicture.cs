namespace SharpMUTerm.Core.Avatars;

/// <summary>
/// A character's or a world's picture, decoded and cut to a square: <paramref name="Size"/>² pixels of straight RGBA,
/// row by row. The client decodes each source once and hands the same instance to every view, so two models that carry
/// one picture are equal (the pixels compare by reference) and a host redraws only when the picture changes.
/// </summary>
/// <param name="Source">The source it was read from, as configured.</param>
/// <param name="Size">The width and height in pixels.</param>
/// <param name="Rgba">The pixels, four bytes each.</param>
public sealed record AvatarPicture(string Source, int Size, byte[] Rgba);
