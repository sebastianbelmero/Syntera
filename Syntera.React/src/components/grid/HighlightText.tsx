import React from 'react';

export interface HighlightTextProps {
  text: string | number | null | undefined;
  highlight: string;
}

/**
 * Highlights matching text segments with <mark>.
 * Used inside AppGrid cells to visually match search keywords.
 */
export const HighlightText: React.FC<HighlightTextProps> = ({ text, highlight }) => {
  if (text === null || text === undefined) return null;
  const str = String(text);
  if (!highlight || highlight.trim() === '') return <>{str}</>;

  const escaped = highlight.replace(/[-[\]{}()*+?.,\\^$|#\s]/g, '\\$&');
  const regex = new RegExp('(' + escaped + ')', 'gi');
  const parts = str.split(regex);

  return (
    <span className="app-grid-highlight">
      {parts.map((p, i) =>
        p.toLowerCase() === highlight.toLowerCase() ? (
          <mark key={i}>{p}</mark>
        ) : (
          p
        )
      )}
    </span>
  );
};
