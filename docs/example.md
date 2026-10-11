# Welcome to InkPage

InkPage is a quiet place to **read** and *write* Markdown on Windows — offline, and with your files left exactly as they were.

> Tip: `Ctrl+1` reads, `Ctrl+2` splits the window into source and preview, `Ctrl+3` edits the source only.

## What it keeps safe

| When you | InkPage keeps |
| --- | --- |
| open a file and close it again | the file untouched: same bytes, same timestamp |
| edit one paragraph | every other line exactly as it was |
| save a GB18030 or UTF-16 file | its encoding, BOM and line endings |

## A little code

```python
def reading_time(words: int, per_minute: int = 230) -> str:
    """Rough reading time for a note."""
    minutes = max(1, round(words / per_minute))
    return f"{minutes} min read"
```

## Today

- [x] Draft the release notes
- [x] Check the tables in split view
- [ ] Try the dark theme

---

More syntax: [CommonMark help](https://commonmark.org/help/).
