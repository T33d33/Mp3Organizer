# Verified synthetic MP3 playlist example

Generated test audio only; no real music was accessed. This example was produced by the actual scan + run commands in the full test suite.

Source playlist: `Playlists\Cuphead OST.m3u8`

| Original entry | Persisted source-file identity | Verified canonical target |
|---|---|---|
| `../Cuphead OST/45 - The Airship.mp3` | `e195966b3d374627817edee7a9b78cce` | `Kristofer Maddigan\2017 - Cuphead OST\45 - The Airship.mp3` |
| `../Cuphead OST/55 - Winner Takes All.mp3` | `9853cccc42a449b2a863fe47dac2dc49` | `Kristofer Maddigan\2017 - Cuphead OST\55 - Winner Takes All.mp3` |
| `../Cuphead OST/45 - The Airship.mp3` | `e195966b3d374627817edee7a9b78cce` | `Kristofer Maddigan\2017 - Cuphead OST\45 - The Airship.mp3` |

Generated: `_Playlist-Folder\[PL-0001] Cuphead OST.m3u8`

```m3u
#EXTM3U
#SOURCE-PLAYLIST-ID:PL-0001
#PLAYABLE:3
#OMITTED:0
#EXTINF:-1,Kristofer Maddigan - The Airship
..\Kristofer Maddigan\2017 - Cuphead OST\45 - The Airship.mp3
#EXTINF:-1,Kristofer Maddigan - Winner Takes All
..\Kristofer Maddigan\2017 - Cuphead OST\55 - Winner Takes All.mp3
#EXTINF:-1,Kristofer Maddigan - The Airship
..\Kristofer Maddigan\2017 - Cuphead OST\45 - The Airship.mp3
```

The repeated Airship entry uses the same persisted source identity and canonical file. Both input audio and the original playlist remained unchanged.

