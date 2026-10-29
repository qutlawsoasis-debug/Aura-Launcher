package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.Track;

import java.util.Collections;
import java.util.List;

/**
 * Immutable snapshot of music state (queue and search results) for QuickMenuScreen.
 * Read safely by render and layout without triggering synchronous IO.
 */
public record QuickMenuMusicSnapshot(List<Track> queue, List<Track> searchResults, String searchQuery) {
    public static final QuickMenuMusicSnapshot EMPTY = new QuickMenuMusicSnapshot(Collections.emptyList(), Collections.emptyList(), "");

    public QuickMenuMusicSnapshot {
        queue = (queue != null && !queue.isEmpty()) ? List.copyOf(queue) : Collections.emptyList();
        searchResults = (searchResults != null && !searchResults.isEmpty()) ? List.copyOf(searchResults) : Collections.emptyList();
        searchQuery = (searchQuery != null) ? searchQuery : "";
    }

    public List<Track> tracksForTab(int tab) {
        return tab == 0 ? queue : searchResults;
    }

    public int countForTab(int tab) {
        return (tab == 0 ? queue : searchResults).size();
    }
}
