# Page media paths

## Single Page
Open Config. File and Folder buttons pick the source; the preview shows supported file counts or a path error. Full paths are available in tooltips. Save validates the paths and persists the configuration. Cancel discards the draft.

## Multiple Pages
1. Select devices and filter the list to the desired Pages or profiles.
2. Check the target rows, then choose **Bulk media paths**.
3. Review the target names. Choose File or Folder for each path you want to replace. Unchecked categories keep their existing values.
4. To remove a path, explicitly check **Clear saved path**.
5. Choose **Apply and save paths**. Counts refresh and the selected configurations are saved to `%AppData%/855Media/facebook_pages.json`.

Supported categories: Reels, Photos, Long videos, Group photos, Group reels, Group videos. Group categories require folders. Paths must exist on the Windows computer and be absolute; quoted paths and environment variables are normalized. Folder counts include matching files at the top level, not subfolders. An empty existing folder is valid and has zero matching files.

Shared source folders are supported. This editor changes paths only; P-Flow's posting history and configured cleanup policies still control media reuse and deletion. Editing is blocked during active fetch/post operations. On persistence failure, the editor stays open with an error and the previous in-memory settings are restored.
