# 欢迎使用 Still mark

一个安静的 Markdown **阅读**与*写作*工具：完全离线，文件原样保留，~~不会偷偷重排格式~~。[^1]

> [!TIP]
> `Ctrl+1` 阅读 · `Ctrl+2` 分栏 · `Ctrl+3` 编辑

## 今天的待办

- [x] 写好更新说明
- [x] 在分栏模式下检查表格
- [ ] 试试深色主题

## 它会替你守住什么

| 你的操作 | Still mark 保证 |
| --- | --- |
| 只打开、不修改就关闭 | 字节和修改时间都不变 |
| 保存 GB18030 文件 | 编码、BOM、换行符都保留 |

## 一小段代码

```python
def read_time(words: int, wpm=300) -> str:
    minutes = max(1, round(words / wpm))
    return f"约 {minutes} 分钟读完"
```

## 写得更快

1. 选中文字，按 `Ctrl+B` 加**粗**，按 `Ctrl+I` 变*斜体*。
2. 在列表项末尾按 `Enter` 会自动续写下一项：
   - 无序列表、编号列表、任务清单和引用都会续写；
   - 在空的一项上再按 `Enter` 就结束列表。
3. `Ctrl+F` 查找，`Ctrl+H` 替换。

> [!NOTE]
> 未保存的内容每 8 秒写入一次恢复草稿。

---

[^1]: 包括换行符、编码和 BOM。更多语法见 [CommonMark 速查](https://commonmark.org/help/)。
