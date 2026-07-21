import { Suspense } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { App as AntApp, ConfigProvider, Spin } from 'antd'
import zhCN from 'antd/locale/zh_CN'
import LobbyPage from './pages/LobbyPage'
import GamePage from './pages/GamePage'
import PlayerPage from './pages/PlayerPage'
import { SessionProvider } from './salasasa/SessionContext'

const theme = {
  token: {
    colorPrimary: '#126e82',
    borderRadius: 10,
    fontFamily: 'CmuSerif, SimKai, "Noto Serif SC", serif',
  },
}

function LoadingScreen() {
  return <div className="app-loading"><Spin size="large" /></div>
}

export default function App() {
  return (
    <ConfigProvider locale={zhCN} theme={theme}>
      <AntApp>
        <BrowserRouter basename="/2d">
          <SessionProvider>
            <Suspense fallback={<LoadingScreen />}>
              <Routes>
                <Route path="/" element={<LobbyPage />} />
                <Route path="/game" element={<GamePage />} />
                <Route path="/player/:key" element={<PlayerPage />} />
                <Route path="*" element={<Navigate to="/" replace />} />
              </Routes>
            </Suspense>
          </SessionProvider>
        </BrowserRouter>
      </AntApp>
    </ConfigProvider>
  )
}
