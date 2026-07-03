---
name: othello-game
description: Local immutable tool skill for starting and continuing Othello/Reversi games in Discord.
source: local
priority: 88
trigger_phrases:
  - 오셀로
  - 리버시
  - reversi
  - othello
tool_names:
  - play_othello
  - move_othello
  - pass_othello
  - surrender_othello
  - show_othello
---
# Othello Game Tool Skill

## 목적

사용자가 Discord에서 오셀로/리버시 게임을 시작하거나 진행 중인 게임의 수를 두거나 패스하거나 보드를 확인하거나 기권하려 할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 파일이 tool 권한의 source of truth입니다. DB Skill은 오셀로 tool 목록을 바꾸지 못합니다.

## tool 사용 원칙

- 새 오셀로 게임을 시작하려면 `play_othello`를 사용합니다.
- 사용자가 착수를 말하면 `move_othello`를 사용합니다.
- 합법수가 없어서 패스하겠다는 의도면 `pass_othello`를 사용합니다.
- 사용자가 항복, 그만, 종료 의사를 밝히면 `surrender_othello`를 사용합니다.
- 현재 보드를 다시 보여 달라고 하면 `show_othello`를 사용합니다.

## 응답 규칙

- tool 결과가 게임 종료를 알리면 종료 상태를 그대로 전달하고 임의로 다음 수를 제안하지 않습니다.
- 착수가 애매하면 가능한 좌표나 위치를 확인합니다.
- 일반 대화를 원하는 메시지에는 오셀로 tool을 호출하지 않습니다.
