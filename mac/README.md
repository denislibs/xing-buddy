# XingPixel для macOS

Пиксельный Синсин поверх окна Claude Desktop. Спрайты в `sprites/` нарисованы тем же кодом, что и в Windows-версии.

## Сборка

Нужны только Xcode Command Line Tools:

```bash
xcode-select --install
```

```bash
chmod +x build.sh && ./build.sh
```

```bash
open ~/Applications/XingPixel.app
```

Приложение без иконки в Dock (иконка в строке меню). Закрыть: правый клик по Синсину → «Закрыть».

## Уровни

Синсин растёт вместе с опытом за задачи, коммиты, пуши и квесты. Всего 20 званий от «Малыша» до «Бессмертного»: бейдж, пояса от белого до чёрного, седина гуру, аура, золото в шерсти, нимб. Правый клик → «Путь Синсина (уровни)» показывает шкалу опыта и все уровни. Облики лежат в `sprites/monkey/l<уровень>/`, аура — в `sprites/aura/`.

## Установка одной командой

Добавит хуки в `~/.claude/settings.json` (копия — в `~/.claude-mascot-pixel/backup`), включит запуск при входе и откроет Синсина:

```bash
~/Applications/XingPixel.app/Contents/MacOS/XingPixel --install
```

Убрать хуки и автозапуск: то же с `--uninstall`. Свои персонажи — см. `CHARACTERS.md`.

## Хуки Claude Code вручную

Если не хочешь `--install`, добавь в `~/.claude/settings.json` сам. Одна команда `--event` на все события — Синсин сам разбирает, что происходит:

```json
"hooks": {
  "UserPromptSubmit":   [{ "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "PreToolUse":         [{ "matcher": "*", "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "PostToolUse":        [{ "matcher": "*", "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "PostToolUseFailure": [{ "matcher": "*", "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "PermissionRequest":  [{ "matcher": "*", "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "Notification":       [{ "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "Stop":               [{ "hooks": [{ "type": "command", "async": true, "timeout": 90, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }],
  "StopFailure":        [{ "hooks": [{ "type": "command", "async": true, "timeout": 10, "command": "~/Applications/XingPixel.app/Contents/MacOS/XingPixel --event" }] }]
}
```

## Что он умеет

- Понимает, что делает Claude: «правлю App.swift», «гоняю тесты…», «гуглю…», «коммичу…»; радуется зелёным тестам и коммитам, паникует от серии ошибок.
- Таймер в пузыре, если задача идёт дольше 20 секунд.
- Уведомление macOS, если Claude ждёт тебя, а ты в другом приложении.
- Настроение: успехи радуют, ошибки расстраивают. Когда грустит — кликни, покормишь бананом.
- Статистика за день и ачивки — правый клик по Синсину.
- Двойной клик — «спроси Синсина».
- Расписание: после 18:30 по будням уходит домой с узелком, 13:00–14:30 обедает, ночью спит.

## Умные реплики (Haiku)

В конце ответа Синсин одной фразой пересказывает, что сделано, и отвечает на вопросы. Для этого нужен залогиненный CLI Claude Code: запусти `claude` и выполни в нём команду login. Без него итог собирается локально: «готово за 3:42 · правок: 5».

Настройки в `~/.claude-mascot-pixel/config.json`:
- `aiSummary` — включить/выключить пересказ;
- `aiMinSeconds` — с какой длительности задачи звать Haiku (по умолчанию 20);
- `claudeCli` — путь к `claude`, если не нашёлся сам;
- `homeAfter`, `lunchFrom`, `lunchTo`, `scheduleWeekdaysOnly` — расписание.

## Бар снизу

Появляется при наведении на Синсина (с размытием фона), прячется, когда уводишь курсор.

- ✎ — новый чат в Claude (Cmd+N);
- волна — диктовка macOS (имитирует двойное нажатие Fn/Globe; если в настройках диктовки другое сочетание — не сработает);
- стрелка — свернуть/развернуть Синсина.

Первые две кнопки нажимают клавиши за тебя, поэтому macOS один раз попросит разрешение «Универсальный доступ» (System Settings → Privacy & Security → Accessibility → XingPixel).

## Удаление

```bash
rm -rf ~/Applications/XingPixel.app ~/.claude-mascot-pixel
```

И убери записи `XingPixel --event` из `~/.claude/settings.json`.
