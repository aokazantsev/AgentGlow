// AgentGlow plugin version=2 port={{PORT}}
// Пересылает состояние сессий OpenCode программе AgentGlow на 127.0.0.1. Тексты запросов и ответов не пересылаются.
// Плагин ничего не читает у OpenCode кроме событий, не обращается к его серверу и не блокирует его: AGENTGLOW_DISABLE=1 отключает.
const VERSION = 2
const EVENT_URL = "http://127.0.0.1:{{PORT}}/opencode"
const SEND_TIMEOUT_MS = 1500
const ACTIVITY_INTERVAL_MS = 20000
const BACKOFF_MS = 10000
const QUEUE_SOFT_LIMIT = 50
const QUEUE_HARD_LIMIT = 200

export const AgentGlow = async ({ directory }) => {
  if (process.env.AGENTGLOW_DISABLE) return {}
  const parents = new Map()
  const announced = new Set()
  const lastActivity = new Map()
  let queue = Promise.resolve()
  let queued = 0
  let backoffUntil = 0

  const send = async (payload) => {
    if (Date.now() < backoffUntil) return
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), SEND_TIMEOUT_MS)
    try {
      await fetch(EVENT_URL, {
        method: "POST",
        headers: { "Content-Type": "application/json", "X-AgentGlow": "1" },
        body: JSON.stringify({ pluginVersion: VERSION, pid: process.pid, directory, ...payload }),
        signal: controller.signal,
      })
    } catch {
      backoffUntil = Date.now() + BACKOFF_MS
    } finally {
      clearTimeout(timer)
    }
  }

  const sendForSession = async (kind, sessionID, extra) => {
    if (!sessionID) return
    await send({ kind, sessionId: sessionID, parentId: parents.get(sessionID) ?? null, ...extra })
  }

  const announce = async (info) => {
    if (!info?.id) return
    const parentID = info.parentID ?? null
    parents.set(info.id, parentID)
    const key = `${info.id}|${parentID ?? ""}|${info.directory ?? ""}`
    if (announced.has(key)) return
    announced.add(key)
    await send({
      kind: "session.created",
      sessionId: info.id,
      parentId: parentID,
      directory: info.directory ?? directory,
    })
  }

  const handle = async (event) => {
    const properties = event.properties ?? {}
    switch (event.type) {
      case "session.created":
      case "session.updated":
        return announce(properties.info)
      case "session.deleted":
        return send({ kind: "session.deleted", sessionId: properties.sessionID ?? properties.info?.id })
      case "session.status": {
        const type = properties.status?.type
        if (type === "busy") return sendForSession("busy", properties.sessionID)
        if (type === "idle") return sendForSession("idle", properties.sessionID)
        if (type === "retry") return sendForSession("retry", properties.sessionID, { retryAt: properties.status.next })
        return
      }
      case "session.idle":
        return sendForSession("idle", properties.sessionID)
      case "session.error":
        return sendForSession("error", properties.sessionID, { errorName: properties.error?.name })
      case "permission.asked":
      case "permission.v2.asked":
        return sendForSession("permission.asked", properties.sessionID, { requestId: properties.id })
      case "permission.replied":
      case "permission.v2.replied":
        return sendForSession("permission.replied", properties.sessionID, { requestId: properties.requestID })
      case "question.asked":
      case "question.v2.asked":
        return sendForSession("question.asked", properties.sessionID, { requestId: properties.id })
      case "question.replied":
      case "question.v2.replied":
        return sendForSession("question.replied", properties.sessionID, { requestId: properties.requestID })
      case "question.rejected":
      case "question.v2.rejected":
        return sendForSession("question.rejected", properties.sessionID, { requestId: properties.requestID })
      case "message.part.updated":
      case "message.part.delta":
        return sendForSession("activity", properties.sessionID ?? properties.part?.sessionID)
      default:
        return
    }
  }

  const isActivity = (event) => event.type === "message.part.updated" || event.type === "message.part.delta"

  const isThrottledActivity = (event) => {
    if (!isActivity(event)) return false
    const properties = event.properties ?? {}
    const sessionID = properties.sessionID ?? properties.part?.sessionID
    if (!sessionID) return true
    const now = Date.now()
    if (now - (lastActivity.get(sessionID) ?? 0) < ACTIVITY_INTERVAL_MS) return true
    lastActivity.set(sessionID, now)
    return false
  }

  const enqueue = (task) => {
    queued += 1
    queue = queue
      .then(task)
      .catch(() => {})
      .then(() => {
        queued -= 1
      })
  }

  enqueue(() => send({ kind: "hello" }))

  return {
    event: async ({ event }) => {
      try {
        if (!event || isThrottledActivity(event)) return
        if (queued >= QUEUE_HARD_LIMIT) return
        if (queued >= QUEUE_SOFT_LIMIT && isActivity(event)) return
        enqueue(() => handle(event))
      } catch {}
    },
  }
}
