# 欢迎使用 InkPage 墨页

InkPage 是一个安静的 Markdown **阅读**与*写作*工具：完全离线，文件原样保留。

> 小提示：`Ctrl+1` 阅读，`Ctrl+2` 分栏（左边源文件、右边预览），`Ctrl+3` 只编辑源文件。

## 它会替你守住什么

| 你的操作 | InkPage 保证 |
| --- | --- |
| 只打开、不修改就关闭 | 文件不被写入，字节和修改时间都不变 |
| 只改了一个段落 | 其他每一行都和原来一模一样 |
| 保存 GB18030 / UTF-16 文件 | 原编码、BOM 和换行符都保留 |

## 一小段代码

```python
def reading_time(words: int, per_minute: int = 300) -> str:
    """估算一篇笔记的阅读时间。"""
    minutes = max(1, round(words / per_minute))
    return f"约 {minutes} 分钟读完"
```

## 今天的待办

- [x] 写好更新说明
- [x] 在分栏模式下检查表格
- [ ] 试试深色主题

---

更多语法见 [CommonMark 速查](https://commonmark.org/help/)。
