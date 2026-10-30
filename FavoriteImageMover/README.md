# Favorite Image Mover

C# console app that moves image files from one folder to another when Windows file metadata says the image is a favorite or has a star rating.

## Run

```powershell
dotnet run --project FavoriteImageMover -- "C:\Photos" "D:\Favorite Photos" --recursive --dry-run
```

Remove `--dry-run` after checking the output:

```powershell
dotnet run --project FavoriteImageMover -- "C:\Photos" "D:\Favorite Photos" --recursive
```

## Options

- `-r`, `--recursive`: include subfolders.
- `--dry-run`: show what would move without changing files.
- `--copy`: copy favorite images instead of moving them.
- `--include-hidden`: include hidden/system files.
- `--extensions .jpg,.png,.webp`: override the image extensions scanned.
- `--verbose`: print skipped files and metadata read errors.

## Favorite Detection

The app checks these Windows Shell metadata fields:

- `System.IsFavorite`
- `System.SimpleRating`
- `System.Rating`

Some photo apps store favorites only in their private app database instead of writing it to the image file metadata. In that case, the app will not see the favorite mark unless the app writes a Windows favorite/rating property to the file.
