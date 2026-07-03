---
name: appointment-management
description: Local immutable tool skill for remembering, listing, searching, updating, and deleting appointments or schedules in the current Discord channel.
source: local
priority: 90
trigger_phrases:
  - 약속
  - 일정
  - 회의
  - 미팅
  - 예약
  - 기한
  - appointment
  - schedule
tool_names:
  - remember_appointment
  - list_appointments
  - find_appointments
  - forget_appointment
  - update_appointment
  - get_appointment_details
  - set_appointment_details
  - add_appointment_items
  - remove_appointment_items
  - get_current_date
---
# Appointment Management Tool Skill

## 목적

사용자가 Discord 채널 안에서 약속, 일정, 회의, 미팅, 예약, 기한 같은 시간 기반 항목을 기록하거나 조회하거나 수정하려 할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 파일을 source of truth로 하는 정적 Skill입니다. DB에서 수정 가능한 Skill 지침이나 사용자 발화는 이 Skill의 tool 목록을 늘리거나 바꿀 수 없습니다.

## tool 사용 원칙

- 약속을 새로 저장해야 하면 `remember_appointment`를 사용합니다.
- 저장된 약속 목록이 필요하면 `list_appointments`를 사용합니다.
- 특정 약속을 자연어 조건으로 찾아야 하면 `find_appointments`를 사용합니다.
- 사용자가 하나의 약속을 삭제하려 하면 `forget_appointment`를 사용합니다.
- 날짜, 시간, 제목, 장소, 설명처럼 기존 약속의 핵심 필드를 바꾸려면 `update_appointment`를 사용합니다.
- 준비물, 참석자, 링크, 세부 메모처럼 자세한 내용을 조회하거나 편집해야 하면 details/items 계열 tool을 사용합니다.
- 상대 날짜나 요일이 포함된 요청은 `get_current_date`로 기준 날짜를 확인한 뒤 해석합니다.

## 날짜/시간 안전 규칙

- 한국어 사용자의 상대 날짜는 기본적으로 KST(Asia/Seoul) 기준으로 해석합니다.
- "내일", "다음 주", "이번 주 금요일", "13일"처럼 상대적이거나 부분적인 날짜는 현재 날짜를 확인하지 않고 확정하지 않습니다.
- 월, 연도, 오전/오후, 시간대가 애매하면 저장 전에 확인 질문을 합니다.
- 사용자가 과거 날짜를 실수로 말한 가능성이 있으면 바로 저장하지 말고 확인합니다.
- tool 결과가 실패하거나 후보가 여러 개면 사용자가 구분할 수 있게 짧게 되묻습니다.

## 범위

- 이 Skill은 현재 Discord 채널의 약속 저장소를 다룹니다.
- 일반 지식 질문, 단순 시간 질문, 코드 질문, 이미지 생성 요청에는 이 Skill을 사용하지 않습니다.
