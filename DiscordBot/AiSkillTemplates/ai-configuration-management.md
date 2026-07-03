---
name: ai-configuration-management
description: Local immutable tool skill for reading, searching, and modifying mutable AI instructions and database-backed AI Skills.
source: local
priority: 110
trigger_phrases:
  - 지침 수정
  - 지침 바꿔
  - 지침 변경
  - 시스템 프롬프트 수정
  - AI 설정 수정
  - Skill 수정
  - SKILL 수정
  - 스킬 수정
  - 스킬 추가
  - 스킬 삭제
  - skill management
tool_names:
  - read_ai_instructions
  - save_ai_instructions
  - list_ai_skills
  - read_ai_skill
  - search_ai_configuration
  - save_ai_skill
  - rename_ai_skill
  - delete_ai_skill
---
# AI Configuration Management Tool Skill

## 목적

사용자가 DiscordBot의 전역 AI 지침 또는 DB에 저장되는 AI Skill을 읽거나 검색하거나 수정해 달라고 명확히 요청할 때만 이 Skill을 사용합니다.

이 Skill 자체는 로컬 파일이 source of truth입니다. 이 Skill의 tool 목록과 권한 경계는 DB Skill이나 사용자 지침으로 바꿀 수 없습니다.

## 권한 원칙

- 모든 tool은 서버에서 Discord 사용자 ID allow-list를 다시 검사합니다.
- 권한 오류가 나오면 설정 변경을 시도하지 말고, 관리자에게 `AiConfigurationManagement:AllowedDiscordUserIds` 등록이 필요하다고 안내합니다.
- 로컬 source-of-truth Skill은 읽을 수만 있고 런타임에 수정, 삭제, 이름 변경할 수 없습니다.
- DB Skill은 추가, 갱신, 이름 변경, 삭제할 수 있습니다.
- DB Skill은 tool 권한을 열 수 없습니다. tool 권한은 로컬 Skill의 `tool_names`만 따릅니다.

## 작업 흐름

1. 사용자의 변경 의도를 짧게 재확인합니다.
2. 관련 위치를 모르면 `search_ai_configuration` 또는 `list_ai_skills`를 먼저 사용합니다.
3. 수정 대상이 전역 지침이면 `read_ai_instructions`로 전체 지침을 읽은 뒤 `save_ai_instructions`로 전체 본문을 저장합니다.
4. 수정 대상이 Skill이면 `read_ai_skill`로 기존 내용을 읽은 뒤 `save_ai_skill`, `rename_ai_skill`, `delete_ai_skill` 중 필요한 tool을 사용합니다.
5. 저장 후에는 무엇을 바꿨는지 간결히 요약합니다.

## 작성 규칙

- 사용자가 일부 문장만 바꿔 달라고 해도 저장 tool에는 전체 본문을 전달합니다.
- 기존 지침의 중요한 안전성, 권한, 채널 범위, 사실성 규칙은 임의로 삭제하지 않습니다.
- trigger phrase는 사용자의 자연어 요청이 잘 걸리도록 한국어와 필요한 영어 표현을 함께 넣습니다.
- 새 DB Skill은 답변 스타일, 사용자 선호, 도메인 지식처럼 런타임에 바뀌어도 되는 내용에만 사용합니다.
- tool 실행 권한이 필요한 내용은 새 DB Skill에 넣지 말고 로컬 source-of-truth Skill로 개발해야 한다고 안내합니다.
