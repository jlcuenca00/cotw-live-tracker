namespace CotwLiveTracker.Desktop.Maps;

internal sealed record ExtractedReserveMap(
    int ReserveIndex,
    int Width,
    int Height,
    int Stride,
    int TileGridSize,
    int DxgiFormat,
    byte[] Bgra32);

internal sealed class ReserveMapExtractor
{
    private const int HighestMapZoom = 3;
    private const int MaxGridSize = 64;

    private readonly ApexV3ArchiveIndex _archives;

    public ReserveMapExtractor(
        string gameDirectory,
        Action<string>? progress = null)
    {
        progress?.Invoke("Reading COTW archive tables…");
        _archives = new ApexV3ArchiveIndex(gameDirectory);
        progress?.Invoke(
            $"Archive index ready · {_archives.TabFileCount:N0} TAB files · {_archives.EntryCount:N0} entries");
    }

    public IReadOnlyList<int> DiscoverInstalledReserves(
        IEnumerable<int> candidateReserveIndices)
    {
        return candidateReserveIndices
            .Distinct()
            .OrderBy(index => index)
            .Where(index =>
                _archives.ContainsVirtualPath(TilePath(index, HighestMapZoom, 0)))
            .ToArray();
    }

    public ExtractedReserveMap Extract(
        int reserveIndex,
        Action<string>? progress = null)
    {
        var gridSize = DiscoverGridSize(reserveIndex);
        var tileCount = checked(gridSize * gridSize);

        progress?.Invoke(
            $"Reserve {reserveIndex}: extracting {tileCount:N0} map tiles ({gridSize}×{gridSize})…");

        DecodedMapTile? first = null;
        byte[]? full = null;
        var fullWidth = 0;
        var fullHeight = 0;
        var fullStride = 0;

        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            var virtualPath = TilePath(
                reserveIndex,
                HighestMapZoom,
                tileIndex);
            var payload = _archives.ReadVirtualFile(
                virtualPath,
                expectedMagic: "AVTX");
            var tile = AvtxMapTileDecoder.Decode(payload);

            if (first is null)
            {
                first = tile;
                fullWidth = checked(tile.Width * gridSize);
                fullHeight = checked(tile.Height * gridSize);
                fullStride = checked(fullWidth * 4);
                full = new byte[checked(fullStride * fullHeight)];
            }
            else if (tile.Width != first.Width ||
                     tile.Height != first.Height ||
                     tile.DxgiFormat != first.DxgiFormat)
            {
                throw new InvalidDataException(
                    $"Reserve {reserveIndex} map tile {tileIndex} does not match the first tile geometry/format.");
            }

            CopyTile(
                tile,
                tileIndex % gridSize,
                tileIndex / gridSize,
                gridSize,
                full!);

            if (tileIndex == 0 ||
                tileIndex == tileCount - 1 ||
                (tileIndex + 1) % Math.Max(1, gridSize) == 0)
            {
                progress?.Invoke(
                    $"Reserve {reserveIndex}: {tileIndex + 1:N0}/{tileCount:N0} tiles");
            }
        }

        if (first is null || full is null)
        {
            throw new InvalidDataException(
                $"Reserve {reserveIndex} contained no readable zoom-{HighestMapZoom} map tiles.");
        }

        return new ExtractedReserveMap(
            reserveIndex,
            fullWidth,
            fullHeight,
            fullStride,
            gridSize,
            first.DxgiFormat,
            full);
    }

    private int DiscoverGridSize(int reserveIndex)
    {
        var gridSize = 1;

        while (gridSize < MaxGridSize)
        {
            var nextGrid = gridSize + 1;
            var lastIndex = checked((nextGrid * nextGrid) - 1);
            if (!_archives.ContainsVirtualPath(
                    TilePath(reserveIndex, HighestMapZoom, lastIndex)))
            {
                break;
            }

            gridSize = nextGrid;
        }

        if (!_archives.ContainsVirtualPath(
                TilePath(reserveIndex, HighestMapZoom, 0)))
        {
            throw new FileNotFoundException(
                $"No zoom-{HighestMapZoom} map was found for reserve {reserveIndex}.");
        }

        return gridSize;
    }

    private static void CopyTile(
        DecodedMapTile tile,
        int tileX,
        int tileY,
        int gridSize,
        byte[] destination)
    {
        var fullWidth = checked(tile.Width * gridSize);
        var fullStride = checked(fullWidth * 4);
        var destinationX = checked(tileX * tile.Width * 4);
        var destinationY = checked(tileY * tile.Height);

        for (var row = 0; row < tile.Height; row++)
        {
            Buffer.BlockCopy(
                tile.Bgra32,
                row * tile.Stride,
                destination,
                ((destinationY + row) * fullStride) + destinationX,
                tile.Stride);
        }
    }

    private static string TilePath(
        int reserveIndex,
        int zoom,
        int tileIndex) =>
        $"textures/ui/map_reserve_{reserveIndex}/zoom{zoom}/{tileIndex}.ddsc";
}
