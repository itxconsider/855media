# Instagram video downloads

In Settings → General, paste a Netscape `cookies.txt` export from a browser logged into Instagram and choose **Apply Instagram cookies**. The export must contain an unexpired Instagram `sessionid` cookie. Enable the existing authentication persistence option to retain it after restarting. Applying Instagram cookies preserves cookies for other sites.

Paste a post, reel, or profile URL into the dashboard's existing download input. Profile URLs such as `https://www.instagram.com/nicktheplumbobby/` load accessible videos across every feed page. The `/username/reels/` form also loads the profile's video feed. Use the existing selection and download queue to choose videos and quality.

Photo-only posts are skipped. A carousel containing multiple videos yields separate queue items, each selecting the corresponding media position when downloading. Stories and photo downloads are outside this feature's scope.

Instagram may require verification or rate-limit requests. Complete verification in the exporting browser and refresh cookies when requested; wait before retrying rate-limit errors. A private profile is accessible only when the exported session has permission. The loader reports errors for missing or repeated pagination cursors instead of presenting an incomplete listing as complete.

Profile listing uses Instagram's web profile-info and paginated user feed endpoints because yt-dlp marks its Instagram profile extractor as broken. Post/reel metadata and media downloads use yt-dlp. These web endpoints can change independently of the app. Instagram tests cover URL routing, pagination, deduplication, malformed responses, and carousel media selection using HTTP fixtures; live authenticated access must also be checked with the user's own cookies.
