﻿using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TMC.Core
{
    public class Tileset
    {
        public const int TileSize = 8;

        /// <summary>
        /// Represents raw pixel data.
        /// </summary>
        public unsafe struct Tile
        {
            /// <summary>
            /// The size, in pixels, of a single <see cref="Tile"/>.
            /// </summary>
            //public const int Size = 8;

            /// <summary>
            /// The pixel data.
            /// </summary>
            public fixed int Pixels[64];

            /// <summary>
            /// Initializes a new instance of the <see cref="Tile"/> struct by copying pixels from the specified array.
            /// </summary>
            /// <param name="pixels">The pixels to be copied.</param>
            public Tile(int[] pixels)
            {
                if (pixels == null)
                    throw new ArgumentNullException(nameof(pixels));

                if (pixels.Length != 64)
                    throw new ArgumentException("Expected 64 bits of pixel data.", nameof(pixels));

                fixed (int* ptr = Pixels)
                {
                    Marshal.Copy(pixels, 0, new IntPtr(ptr), 64);
                }
            }

            /// <summary>
            /// Gets or sets the specified pixel value.
            /// </summary>
            /// <param name="x"></param>
            /// <param name="y"></param>
            /// <returns></returns>
            public int this[int x, int y]
            {
                get
                {
                    if (x < 0 || x >= 8)
                        throw new ArgumentOutOfRangeException(nameof(x));

                    if (y < 0 || y >= 8)
                        throw new ArgumentOutOfRangeException(nameof(y));

                    fixed (int* pixels = Pixels)
                    {
                        return pixels[x + y * 8];
                    }
                }
                set
                {
                    if (x < 0 || x >= 8)
                        throw new ArgumentOutOfRangeException(nameof(x));

                    if (y < 0 || y >= 8)
                        throw new ArgumentOutOfRangeException(nameof(y));

                    fixed (int* pixels = Pixels)
                    {
                        pixels[x + y * 8] = value;
                    }
                }
            }

            /// <summary>
            /// Determines whether this tile value is equivalent to the specified tile value with the specified flipping.
            /// </summary>
            /// <param name="other">The tile to compare to.</param>
            /// <param name="flipX">Determines whether <paramref name="other"/> is to be flipped from left to right.</param>
            /// <param name="flipY">Determines whether <paramref name="other"/> is to be flipped from top to bottom.</param>
            /// <returns><c>true</c> if the tiles are equivalent; otherwise, <c>false</c>.</returns>
            public bool CompareTo(ref Tile other, bool flipX = false, bool flipY = false)
            {
                fixed (int* src = Pixels)
                fixed (int* dst = other.Pixels)
                {
                    for (int srcY = 0; srcY < 8; srcY++)
                    {
                        for (int srcX = 0; srcX < 8; srcX++)
                        {
                            var dstX = flipX ? (7 - srcX) : srcX;
                            var dstY = flipY ? (7 - srcY) : srcY;

                            if (src[srcX + srcY * 8] != dst[dstX + dstY * 8])
                                return false;
                        }
                    }
                }

                return true;
            }
        }

        private Tile[] tiles;
        private Color[] palette;

        /// <summary>
        /// Initializes a new instance of the <see cref="Tileset"/> class from the specified source image.
        /// </summary>
        /// <param name="source">The source image.</param>
        public Tileset(Bitmap source)
        {
            if (source.Width < 8 || source.Height < 8)
                throw new ArgumentException("Image must be at least 8x8 pixels.", nameof(source));

            if (source.Width / 8 * source.Height / 8 > 0x400)
                throw new ArgumentException("Image is too large, ensure it has no more than 0x400 (1024) tiles.", nameof(source));

            using (var fb = FastBitmap.FromImage(source))
            {
                // Copies a single tile from the source image
                void CopyTile(ref Tile tile, int x, int y)
                {
                    for (int j = 0; j < 8; j++)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            // Find the index of the palette
                            var pixel = palette.IndexOf(source.GetPixel(x + i, y + j));
                            if (pixel < 0) // NOTE: a well formed palette will never trigger this
                                throw new IndexOutOfRangeException();

                            // Copy to the tile
                            tile[i, j] = pixel;
                        }
                    }
                }

                // Initialize palette
                if ((source.PixelFormat & PixelFormat.Indexed) == PixelFormat.Indexed)
                {
                    // Copy the colors from the existing palette
                    switch (source.PixelFormat)
                    {
                        case PixelFormat.Format1bppIndexed:
                            palette = new Color[1 << 1];
                            break;

                        case PixelFormat.Format4bppIndexed:
                            palette = new Color[1 << 4];
                            break;

                        case PixelFormat.Format8bppIndexed:
                            palette = new Color[1 << 8];
                            source.Palette.Entries.CopyTo(palette, 0);
                            break;

                        default:
                            throw new ArgumentException("Unsupported image format.", nameof(source));
                    }

                    source.Palette.Entries.CopyTo(palette, 0);
                }
                else
                {
                    // Create a new palette
                    palette = Core.Palette.Create(fb);
                }

                // Initialize tiles
                var width = source.Width / 8;
                var height = source.Height / 8;

                tiles = new Tile[width * height];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        CopyTile(ref tiles[x + y * width], x * 8, y * 8);
                    }
                }
            }
        }

        /// <summary>
        /// Initialzies a new instance of the <see cref="Tileset"/> with the specified tile array.
        /// </summary>
        /// <param name="tiles">The tiles.</param>
        protected Tileset(Tile[] tiles, Color[] palette)
        {
            this.tiles = tiles;
            this.palette = palette;
        }

        #region Methods

        /// <summary>
        /// Gets the specified tile.
        /// </summary>
        /// <param name="index">The index of the tile.</param>
        /// <returns></returns>
        public ref Tile this[int index] => ref tiles[index];

        /// <summary>
        /// Creates a new <see cref="Tileset"/> from the specified source image.
        /// </summary>
        /// <param name="bmp">The source image.</param>
        /// <param name="allowFlipping">Determines whether tile flipping is permitted.</param>
        /// <returns></returns>
        public static (Tileset Tileset, Tilemap Tilemap) Create(Bitmap bmp, bool allowFlipping)
        {
            using (var fb = FastBitmap.FromImage(bmp))
            {
                return Create(fb, allowFlipping);
            }
        }

        /// <summary>
        /// Creates a new <see cref="Tileset"/> from the specified source image.
        /// </summary>
        /// <param name="fb">The source image.</param>
        /// <param name="allowFlipping">Determines whether tile flipping is permitted.</param>
        public static (Tileset Tileset, Tilemap Tilemap) Create(FastBitmap fb, bool allowFlipping)
        {
            var width = fb.Width / 8;
            var height = fb.Height / 8;

            // Create initial tileset (with all tiles)
            var tileset = new Tileset(fb);

            // Create empty tilemap
            var tilemap = new Tilemap(width, height);

            // Define the first tile
            tilemap[0] = new Tilemap.Tile();

            // Scan the tileset for repeated tiles
            var tiles = new List<Tile> { tileset[0] };
            var current = 1;

            for (int i = 1; i < tileset.Length; i++)
            {
                ref var tile = ref tileset[i];

                // The current tile
                var index = current;
                var flipX = false;
                var flipY = false;

                // Compare the tile against all unique tiles
                for (int j = 0; j < tiles.Count; j++)
                {
                    var other = tiles[j];

                    // Test tile configurations
                    if (tile.CompareTo(ref other, false, false))
                    {
                        index = j;
                        break;
                    }
                    else if (allowFlipping)
                    {
                        if (tile.CompareTo(ref other, true, false))
                        {
                            index = j;
                            flipX = true;
                            break;
                        }

                        if (tile.CompareTo(ref other, false, true))
                        {
                            index = j;
                            flipY = true;
                            break;
                        }

                        if (tile.CompareTo(ref other, true, true))
                        {
                            index = j;
                            flipX = true;
                            flipY = true;
                            break;
                        }
                    }
                }

                // Update the tilemap
                tilemap[i] = new Tilemap.Tile((short)index, flipX, flipY);

                // Update the tileset
                if (index >= current)
                {
                    tiles.Add(tile);
                    current++;
                }
            }

            // The process is now finished
            return (new Tileset(tiles.ToArray(), tileset.palette), tilemap);
        }

        /// <summary>
        /// Creates a new <see cref="FastBitmap"/> representing all tiles.
        /// </summary>
        /// <param name="columns">The number of columns in a single row of tiles.</param>
        /// <returns></returns>
        public FastBitmap ToImage(int columns)
        {
            if (columns <= 0)
                throw new ArgumentOutOfRangeException(nameof(columns));

            var rows = (tiles.Length / columns) + (tiles.Length % columns > 0 ? 1 : 0);
            var fb = new FastBitmap(columns * 8, rows * 8);

            for (int i = 0; i < tiles.Length; i++)
            {
                // Get the destination
                var x = i % columns;
                var y = i / columns;

                // Get the tile to draw
                ref var tile = ref tiles[i];

                // Draw the tile
                for (int j = 0; j < 8; j++)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        fb.SetPixel(x * 8 + k, y * 8 + j, palette[tile[k, j]]);
                    }
                }
            }

            return fb;
        }

        /// <summary>
        /// Returns an array of column values that will result in a perfect tileset.
        /// </summary>
        /// <returns></returns>
        public int[] GetPerfectColumns()
        {
            var columns = new List<int>();

            for (int i = 1;i <= tiles.Length; i++)
            {
                if (tiles.Length % i == 0) columns.Add(i);
            }

            return columns.ToArray();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of tiles.
        /// </summary>
        public int Length => tiles.Length;

        /// <summary>
        /// Gets the palette.
        /// </summary>
        public Color[] Palette => palette;

        #endregion
    }
}
