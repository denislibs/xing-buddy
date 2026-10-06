# Персонажи-плагины

Синсин встроен и всегда доступен (с аксессуарами). Любой другой персонаж — это папка в `characters/`:

- Windows: `<папка с xing-pixel.exe>\characters\<id>\`
- macOS: `~/.claude-mascot-pixel/characters/<id>/` (или внутри приложения, `Contents/Resources/characters`)

После добавления папки выбери персонажа в «Настройки… → Кто живёт рядом с Claude».

## character.json

```json
{
  "name": "Мини-пиг",
  "author": "ты",
  "pixelArt": true,
  "fps": 9,
  "frames": 48,
  "sound": "pig",
  "lines": { "idle": "хрю-хрю", "error": "хрю?!", "success": "хрю! готово" },
  "poses": {
    "idle": "idle.png",
    "work": { "file": "work.gif" },
    "error": { "file": "error.png", "frames": 12 }
  }
}
```

- `poses` — файл на позу: **PNG-лента** (кадры одинаковой ширины слева направо, количество — `frames` у позы или общее) или **анимированный GIF/WebP**.
- `pixelArt` — `true` для пиксель-арта (масштаб без сглаживания), `false` для фото/гифок.
- `fps` — скорость проигрывания; `sound` — голос 8-битных звуков: `monkey`, `pig` или `spark`.
- `lines` — свои реплики для состояний `idle`, `thinking`, `working`, `success`, `error`, `waiting`.

## Какие позы бывают

Нужна хотя бы одна поза — лучше `idle`. Чего нет, заменяется похожим: git-позы и `bash`, `type`, `read`, `run` → `work` → `idle`; `done`, `banana`, `love`, `stretch` → `happy` → `idle`; `water`, `lunch` → `milk` → `idle`; `home` → `wave`.

| Поза | Когда |
|---|---|
| `idle` | простой |
| `think` | Claude думает |
| `work`, `type`, `read`, `bash`, `run` | работа: код, правки, чтение, терминал, субагенты |
| `done`, `happy`, `love`, `banana`, `wave` | успех, клики, ачивки |
| `error`, `wait` | ошибка, ждёт тебя |
| `sleep`, `milk`, `water`, `lunch`, `stretch` | простой, ночь, обед, забота о здоровье |
| `home` | уходит домой (проигрывается один раз) |
| `git_commit`, `git_push`, `git_pull`, `git_fetch`, `git_merge`, `git_conflict`, `git_rebase`, `git_branch`, `git_stash`, `git_log`, `git_diff`, `git_status`, `git_reset`, `git_tag`, `git_cherry`, `git_pr`, `git_ci` | git-команды |

Примеры — папки `pig` и `spark`: их генерирует `build.ps1` (`xing-pixel.exe --export-character pig <папка>`).
