"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import type { PlayerListItem } from "@/lib/api";

const apiBase = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5044";

export function PlayerSearch() {
  const [query, setQuery] = useState("");
  const [results, setResults] = useState<PlayerListItem[]>([]);

  useEffect(() => {
    if (query.trim().length < 2) {
      setResults([]);
      return;
    }

    const controller = new AbortController();
    const timeout = window.setTimeout(async () => {
      const response = await fetch(`${apiBase}/api/players?search=${encodeURIComponent(query)}&limit=6`, {
        signal: controller.signal
      });

      if (response.ok) {
        setResults((await response.json()) as PlayerListItem[]);
      }
    }, 150);

    return () => {
      controller.abort();
      window.clearTimeout(timeout);
    };
  }, [query]);

  return (
    <div className="player-search">
      <input
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        placeholder="Search CMC, Puka, Andrews..."
        aria-label="Search players"
      />
      {results.length > 0 ? (
        <div className="search-results">
          {results.map((player) => (
            <Link href={`/players/${player.id}`} key={player.id} onClick={() => setQuery("")}>
              <strong>{player.name}</strong>
              <span>{player.team} {player.position}</span>
            </Link>
          ))}
        </div>
      ) : null}
    </div>
  );
}
