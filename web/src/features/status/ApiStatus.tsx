import { useEffect, useState } from 'react'
import { isApiHealthy } from '../../shared/api/health.ts'

type Status = 'checking' | 'healthy' | 'unavailable'

const messages: Record<Status, string> = {
  checking: 'Checking the API…',
  healthy: 'API is healthy',
  unavailable: 'API is unavailable',
}

export function ApiStatus() {
  const [status, setStatus] = useState<Status>('checking')

  useEffect(() => {
    let active = true
    isApiHealthy().then((healthy) => {
      if (active) setStatus(healthy ? 'healthy' : 'unavailable')
    })
    return () => {
      active = false
    }
  }, [])

  return (
    <p role="status" aria-label="API status" className={`api-status api-status--${status}`}>
      {messages[status]}
    </p>
  )
}
