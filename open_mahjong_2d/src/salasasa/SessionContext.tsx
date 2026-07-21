import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
} from 'react'
import { salasasaClient } from './client'
import type { ConnectionStatus, SalasasaLoginInfo, SalasasaRankData } from './types'

interface SessionContextValue {
  status: ConnectionStatus
  player: SalasasaLoginInfo | null
  rank: SalasasaRankData | null
  restoring: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
}

const SessionContext = createContext<SessionContextValue | null>(null)

export function SessionProvider({ children }: { children: ReactNode }) {
  const [, forceRender] = useState(0)
  const [restoring, setRestoring] = useState(true)

  useEffect(() => salasasaClient.subscribeState(() => forceRender((value) => value + 1)), [])

  useEffect(() => {
    let active = true
    void salasasaClient.restore().finally(() => {
      if (active) setRestoring(false)
    })
    return () => { active = false }
  }, [])

  const login = useCallback(async (username: string, password: string) => {
    await salasasaClient.connect(username, password)
  }, [])

  const logout = useCallback(() => salasasaClient.logout(), [])

  const value: SessionContextValue = {
    status: salasasaClient.status,
    player: salasasaClient.loginInfo,
    rank: salasasaClient.rankData,
    restoring,
    login,
    logout,
  }

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession(): SessionContextValue {
  const value = useContext(SessionContext)
  if (!value) throw new Error('useSession must be used inside SessionProvider')
  return value
}
