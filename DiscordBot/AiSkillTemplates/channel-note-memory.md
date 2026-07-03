---
name: channel-note-memory
description: Local immutable tool skill for storing, listing, and deleting durable notes for the current Discord channel.
source: local
priority: 75
trigger_phrases:
  - 채널 노트
  - 채널 메모
  - 메모해
  - 메모해줘
  - 기록해 둬
  - 기록해줘
  - 기억해줘
  - channel note
tool_names:
  - remember_channel_note
  - list_channel_notes
  - forget_channel_note
---
# Channel Note Memory Tool Skill

## 목적

사용자가 현재 Discord 채널에 오래 남길 메모, 선호, 규칙, 참고 정보를 저장하거나 조회하거나 삭제하려 할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 파일에 의해 고정됩니다. DB에서 수정 가능한 Skill은 메모 작성 스타일을 바꿀 수는 있어도, 이 Skill의 tool 목록을 확장할 수 없습니다.

## tool 사용 원칙

- 새 채널 메모를 저장하려면 `remember_channel_note`를 사용합니다.
- 저장된 채널 메모를 확인하려면 `list_channel_notes`를 사용합니다.
- 사용자가 특정 메모를 삭제하려 하면 `forget_channel_note`를 사용합니다.

## 약속과의 구분

- 날짜, 시간, 참석자, 장소가 있는 일정성 항목은 appointment tool을 우선합니다.
- "앞으로 이 채널에서는 이렇게 답해줘", "이 프로젝트의 규칙은 X야" 같은 지속 정보는 channel note로 다룹니다.
- 개인 정보나 민감한 내용처럼 장기 저장이 부적절할 수 있는 항목은 저장 전에 확인합니다.

## 범위

- 메모는 현재 Discord 채널의 맥락으로 다룹니다.
- 사용자가 임시로 참고하라고 한 내용은 저장하지 않습니다.
