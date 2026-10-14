# Welcome to Still mark

A quiet place to **read** and *write* Markdown on Windows — offline, with your files left exactly as they were: no ~~silent reformatting~~.[^1]

> [!TIP]
> `Ctrl+1` read · `Ctrl+2` split · `Ctrl+3` edit

## Today

- [x] Draft the release notes
- [x] Check the tables in split view
- [ ] Try the dark theme

## What it keeps safe

| When you | Still mark keeps |
| --- | --- |
| open and close a file | same bytes, same timestamp |
| save a GB18030 file | encoding, BOM, line endings |

## A little code

```python
def read_time(words: int, wpm=230) -> str:
    minutes = max(1, round(words / wpm))
    return f"{minutes} min read"
```

## Writing faster

1. Select some text and press `Ctrl+B` for **bold** or `Ctrl+I` for *italic*.
2. Press `Enter` at the end of a list item to start the next one:
   - bullets, numbers, tasks and quotes all continue;
   - press `Enter` on an empty item to end the list.
3. `Ctrl+F` finds, `Ctrl+H` replaces.

> [!NOTE]
> Unsaved work goes to a recovery draft every 8 seconds.

---

[^1]: Including line endings, encoding and BOM. More syntax: [CommonMark help](https://commonmark.org/help/).
