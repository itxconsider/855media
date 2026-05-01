# Facebook Bulk Downloader v1

## Install
1. Open `chrome://extensions`
2. Enable Developer mode
3. Load unpacked: `C:\Users\lolie\source\repos\MediaTag\facebook-bulk-downloader-v1`

## Workflow
1. Open a Facebook photos page/profile in current tab
2. Popup -> `Collect Start`
3. Wait while auto-scroll loads content
4. `Collect Stop`
5. `Add To Queue`
6. Set config (concurrency/folder/template)
7. `Download Start`

## Template fields
- `{index}`
- `{caption}`
- `{date}`
- `{photo_id}`
- `{username}`

Example: `{index}_{caption}`

## Export
- `Export CSV`: url + caption + metadata
- `Copy url | caption`: paste into MediaTag
