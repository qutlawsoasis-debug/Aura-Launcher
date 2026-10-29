package net.auramc.glass.client.quickmenu.music;

import net.auramc.glass.client.music.Track;

import java.util.Collections;
import java.util.List;
import java.util.Objects;

/**
 * Immutable snapshot of music state (queue and search results) for QuickMenuScreen.
 * Read safely by render and layout without triggering synchronous IO.
 * Carries searchEpoch, searchQuery, and searchError state for strict epoch matching.
 */
public record QuickMenuMusicSnapshot(
        List<Track> queue,
        List<Track> searchResults,
        String searchQuery,
        long searchEpoch,
        boolean searchError
) {
    public static final QuickMenuMusicSnapshot EMPTY = new QuickMenuMusicSnapshot(
            Collections.emptyList(),
            Collections.emptyList(),
            "",
            0L,
            false
    );

    public QuickMenuMusicSnapshot {
        queue = (queue != null && !queue.isEmpty()) ? List.copyOf(queue) : Collections.emptyList();
        searchResults = (searchResults != null && !searchResults.isEmpty()) ? List.copyOf(searchResults) : Collections.emptyList();
        searchQuery = (searchQuery != null) ? searchQuery : "";
    }

    public QuickMenuMusicSnapshot(List<Track> queue, List<Track> searchResults, String searchQuery) {
        this(queue, searchResults, searchQuery, 0L, false);
    }

    public QuickMenuMusicSnapshot(List<Track> queue, List<Track> searchResults, String searchQuery, long searchEpoch) {
        this(queue, searchResults, searchQuery, searchEpoch, false);
    }

    public boolean hasSearchError() {
        return searchError;
    }

    public boolean matchesSearch(String query, long epoch) {
        return this.searchEpoch == epoch && Objects.equals(this.searchQuery, query != null ? query : "");
    }

    public List<Track> tracksForTab(int tab) {
        return tab == 0 ? queue : searchResults;
    }

    public int countForTab(int tab) {
        return (tab == 0 ? queue : searchResults).size();
    }
}
