---
name: chat-history-retrieval
description: Local immutable tool skill for reading, searching, summarizing, and contextualizing previous messages in the current Discord channel.
source: local
priority: 80
trigger_phrases:
  - 이전 대화
  - 과거 대화
  - 채팅 기록
  - 대화 기록
  - 대화 검색
  - 최근 논의
  - 전에 말한
  - 아까 말한
  - chat history
tool_names:
  - get_chat_history
  - search_chat_history
  - summarize_recent_discussion
  - get_chat_by_message_id
  - get_reply_thread_context
  - get_chat_context
---
# Chat History Retrieval Tool Skill

## 목적

사용자가 현재 Discord 채널에서 이전 메시지, 최근 논의, 특정 발언, 답장 스레드 맥락을 찾아 달라고 할 때만 이 Skill을 사용합니다.

이 Skill은 로컬 파일이 tool 권한의 source of truth입니다. DB Skill 지침은 채팅 기록 조회 tool을 추가로 허용할 수 없습니다.

## tool 사용 원칙

- 최근 대화를 그대로 훑어야 하면 `get_chat_history`를 사용합니다.
- 키워드나 주제로 과거 메시지를 찾아야 하면 `search_chat_history`를 사용합니다.
- 최근 논의의 요약이 필요하면 `summarize_recent_discussion`을 사용합니다.
- 메시지 ID가 명확하면 `get_chat_by_message_id`를 사용합니다.
- 사용자가 답장으로 문맥을 이어가면 `get_reply_thread_context` 또는 `get_chat_context`를 사용합니다.

## 채널 범위 규칙

- 현재 Discord 채널에 저장된 메시지만 조회합니다.
- 다른 채널의 메시지, DM, 서버 전체 기록을 조회할 수 있다고 말하지 않습니다.
- tool 결과에 없는 내용을 기억으로 지어내지 않습니다.

## 응답 규칙

- 조회 결과가 있으면 근거가 되는 메시지의 시간, 작성자, 핵심 내용을 함께 정리합니다.
- 검색 결과가 부족하면 어떤 키워드나 기간이 더 필요한지 짧게 묻습니다.
