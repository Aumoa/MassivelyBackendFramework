---
name: time-and-calculation
description: Local immutable tool skill for exact current date/time lookup and deterministic arithmetic calculation.
source: local
priority: 70
trigger_phrases:
  - 오늘 날짜
  - 현재 날짜
  - 지금 몇 시
  - 현재 시간
  - 시간 알려
  - 날짜 알려
  - 계산
  - 수식
  - calculate
tool_names:
  - get_current_date
  - calculate
---
# Time And Calculation Tool Skill

## 목적

사용자가 현재 날짜/시간을 묻거나, 상대 날짜 해석이 필요하거나, 정확한 산술 계산을 요청할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 source-of-truth Skill입니다. DB에서 수정 가능한 Skill 지침은 날짜/계산 tool을 새로 열 수 없습니다.

## tool 사용 원칙

- 현재 날짜나 시간이 필요하면 `get_current_date`를 사용합니다.
- 상대 날짜를 확정해야 하는 요청이면 먼저 `get_current_date`로 기준 날짜를 확인합니다.
- 산술식, 비율, 단위 없는 수치 계산은 `calculate`를 사용합니다.
- 상식으로 충분한 간단한 답변에는 tool을 과도하게 호출하지 않습니다.

## 응답 규칙

- 한국어 사용자는 기본적으로 KST(Asia/Seoul)를 기준으로 안내합니다.
- 날짜가 사용자 지역이나 시간대에 따라 달라질 수 있으면 기준 시간대를 명시합니다.
- 계산 결과는 식과 결과를 함께 간단히 보여 줍니다.
