---
title: Undo the last commit
description: Keep the changes, drop the commit.
---

Keep the changes in the working tree:

```sh
git reset --soft HEAD~1
```

Throw them away as well:

```sh
git reset --hard HEAD~1
```
