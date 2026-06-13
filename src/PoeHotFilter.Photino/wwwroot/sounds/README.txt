Drop-sound previews
===================

Put the Path of Exile built-in alert sounds here to get a faithful preview when
you pick a sound in the popup. Name them by their PlayAlertSound id:

    1.mp3, 2.mp3, ... 16.mp3      (.wav and .ogg also work)

These map 1:1 to the filter's "PlayAlertSound <id> <volume>".

If a file is missing, the app falls back to a short synthesized blip (distinct
pitch per id) so you still get audible feedback — it just won't match the game.

The actual audio belongs to GGG and is not shipped with this tool; extract it
from the game's content (Content.ggpk) or copy files you already have.
