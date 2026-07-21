import { useEffect, useMemo, useState } from 'react'
import { Button, Card, Form, Input, Modal, Space, Spin, Table, Tag, Typography, message } from 'antd'
import { CrownOutlined, LoginOutlined, LogoutOutlined, ReloadOutlined, TeamOutlined, UserOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { leaderboardUrl, publicApiGet } from '../salasasa/api'
import { salasasaClient } from '../salasasa/client'
import { useSession } from '../salasasa/SessionContext'
import type { PublicLeaderboardEntry } from '../salasasa/types'
import './LobbyPage.css'

const TIERS = [
  { key: 'beginner', title: '初级场', note: '适合熟悉国标规则与平台操作的玩家' },
  { key: 'intermediate', title: '中级场', note: '段位1级及以上可入场' },
  { key: 'advanced', title: '高级场', note: '高段位国标竞技房间' },
  { key: 'mcrpl', title: 'MCRPL', note: '仅限已取得 MCRPL 资格的玩家' },
] as const

const FORMATS = [
  { key: 'dongfeng', title: '东风局', rounds: '4 小局' },
  { key: 'banzhuang', title: '半庄', rounds: '8 小局' },
  { key: 'quanzhuang', title: '全庄', rounds: '16 小局' },
] as const

type QueueStatus = Record<string, { waiting: number; playing: number }>

export default function LobbyPage() {
  const navigate = useNavigate()
  const { status, player, rank, restoring, login, logout } = useSession()
  const [loginOpen, setLoginOpen] = useState(false)
  const [loginBusy, setLoginBusy] = useState(false)
  const [queueStatus, setQueueStatus] = useState<QueueStatus>({})
  const [joinedQueue, setJoinedQueue] = useState<string | null>(null)
  const [matchFound, setMatchFound] = useState(false)
  const [leaders, setLeaders] = useState<PublicLeaderboardEntry[]>([])
  const [leadersBusy, setLeadersBusy] = useState(true)

  const refreshLeaderboard = async () => {
    setLeadersBusy(true)
    try {
      setLeaders(await publicApiGet<PublicLeaderboardEntry[]>(leaderboardUrl(20)))
    } catch (error) {
      message.error(error instanceof Error ? error.message : '排行榜加载失败')
    } finally {
      setLeadersBusy(false)
    }
  }

  useEffect(() => {
    let active = true
    void publicApiGet<PublicLeaderboardEntry[]>(leaderboardUrl(20))
      .then((data) => { if (active) setLeaders(data) })
      .catch((error) => { if (active) message.error(error instanceof Error ? error.message : '排行榜加载失败') })
      .finally(() => { if (active) setLeadersBusy(false) })
    return () => { active = false }
  }, [])

  useEffect(() => salasasaClient.subscribe((response) => {
    if (response.type === 'match/queue_status' && response.queue_status) {
      setQueueStatus(response.queue_status)
    }
    if (response.type === 'match/join_queue_done' && response.success) {
      message.success(response.message || '已加入匹配')
    }
    if (response.type === 'match/leave_queue_done' && response.success) {
      setJoinedQueue(null)
      setMatchFound(false)
      message.success(response.message || '已取消匹配')
    }
    if (response.type === 'match/match_found') {
      setMatchFound(true)
      message.success(`匹配成功：${response.message || '国标排位'}，即将开局`)
    }
    if (response.type === 'tips' && response.message) {
      if (response.success === false) setJoinedQueue(null)
      message[response.success === false ? 'error' : 'info'](response.message)
    }
    if (response.type === 'gamestate/guobiao/game_start') navigate('/game')
  }), [navigate])

  useEffect(() => {
    if (status !== 'online') return
    const refresh = () => salasasaClient.send({ type: 'match/get_queue_status' })
    refresh()
    const timer = window.setInterval(refresh, 5_000)
    return () => window.clearInterval(timer)
  }, [status])

  const queueCards = useMemo(() => TIERS.flatMap((tier) => FORMATS.map((format) => {
    const key = `${tier.key}_${format.key}`
    return { ...tier, format: format.title, rounds: format.rounds, queueKey: key, status: queueStatus[key] }
  })), [queueStatus])

  const joinQueue = (queueKey: string) => {
    if (!player) { setLoginOpen(true); return }
    if (!salasasaClient.send({ type: 'match/join_queue', queue_type: queueKey })) {
      message.error('游戏连接尚未就绪')
      return
    }
    setJoinedQueue(queueKey)
  }

  const leaveQueue = () => {
    if (!salasasaClient.send({ type: 'match/leave_queue' })) message.error('游戏连接尚未就绪')
  }

  const handleLogout = () => {
    setJoinedQueue(null)
    setMatchFound(false)
    logout()
  }

  const submitLogin = async (values: { username: string; password: string }) => {
    setLoginBusy(true)
    try {
      await login(values.username, values.password)
      setLoginOpen(false)
      message.success('登录成功')
    } catch (error) {
      message.error(error instanceof Error ? error.message : '登录失败')
    } finally {
      setLoginBusy(false)
    }
  }

  return (
    <div className="lobby-page">
      <header className="lobby-header">
        <div className="lobby-brand">
          <img src={`${import.meta.env.BASE_URL}logo512.png`} alt="MMCR" />
          <div><strong>Salasasa 2D</strong><span>国标麻将 · MMCR 桌面</span></div>
        </div>
        <Space wrap>
          {restoring && <Spin size="small" />}
          {player ? (
            <>
              <Tag color={status === 'online' ? 'green' : 'orange'}>{status === 'online' ? '已连接' : '重连中'}</Tag>
              <Button icon={<UserOutlined />} onClick={() => navigate(`/player/${player.user_id}`)}>{player.username}</Button>
              <Button icon={<LogoutOutlined />} onClick={handleLogout}>退出</Button>
            </>
          ) : <Button type="primary" icon={<LoginOutlined />} onClick={() => setLoginOpen(true)}>登录</Button>}
        </Space>
      </header>

      <main className="lobby-main">
        <section className="lobby-hero">
          <div>
            <Typography.Title level={1}>同一牌桌，两种视角</Typography.Title>
            <Typography.Paragraph>2D 网页端与现有 Unity 3D 客户端共用原有登录、匹配和国标对局服务。</Typography.Paragraph>
          </div>
          <div className="rank-chip"><CrownOutlined /><span>当前段位</span><strong>{rank?.guobiao_rank ?? '登录后查看'}</strong><small>{rank ? `${rank.guobiao_score} 分` : '国标排位'}</small></div>
        </section>

        {joinedQueue && (
          <Card className="queue-banner">
            <Space wrap>
              <Spin />
              <strong>{matchFound ? '匹配成功，正在准备牌桌…' : `正在匹配：${joinedQueue}`}</strong>
              {!matchFound && <Button danger onClick={leaveQueue}>取消匹配</Button>}
            </Space>
          </Card>
        )}

        <div className="lobby-grid">
          <section>
            <div className="section-title"><div><h2>匹配房间</h2><p>仅支持国标规则；段位和资格由现有服务端校验。</p></div><Tag color="blue">12 个排位队列</Tag></div>
            <div className="queue-grid">
              {queueCards.map((queue) => (
                <Card key={queue.queueKey} className={`queue-card tier-${queue.queueKey.split('_')[0]}`} hoverable>
                  <div className="queue-card__top"><strong>{queue.title}</strong><Tag>{queue.format}</Tag></div>
                  <p>{queue.note}</p>
                  <div className="queue-card__counts"><span><TeamOutlined /> 等待 {queue.status?.waiting ?? 0}</span><span>对局中 {queue.status?.playing ?? 0}</span></div>
                  <Button type="primary" block disabled={Boolean(joinedQueue) || status !== 'online'} onClick={() => joinQueue(queue.queueKey)}>{queue.rounds} · 开始匹配</Button>
                </Card>
              ))}
            </div>
          </section>

          <aside>
            <Card className="leaderboard-card" title={<span><CrownOutlined /> 国标排行榜</span>} extra={<Button type="text" icon={<ReloadOutlined />} loading={leadersBusy} onClick={() => void refreshLeaderboard()} />}>
              <Table<PublicLeaderboardEntry>
                rowKey="user_id"
                size="small"
                loading={leadersBusy}
                pagination={false}
                dataSource={leaders}
                onRow={(record) => ({ onClick: () => navigate(`/player/${record.user_id}`) })}
                columns={[
                  { title: '#', dataIndex: 'rank_position', width: 44 },
                  { title: '玩家', dataIndex: 'username', ellipsis: true },
                  { title: '段位', dataIndex: 'guobiao_rank', width: 78 },
                ]}
              />
            </Card>
          </aside>
        </div>
      </main>

      <Modal title="登录 Salasasa" open={loginOpen} footer={null} onCancel={() => setLoginOpen(false)} destroyOnHidden>
        <Form layout="vertical" onFinish={(values) => void submitLogin(values)}>
          <Form.Item name="username" label="用户名" rules={[{ required: true, message: '请输入用户名' }]}><Input autoComplete="username" /></Form.Item>
          <Form.Item name="password" label="密码" rules={[{ required: true, message: '请输入密码' }]}><Input.Password autoComplete="current-password" /></Form.Item>
          <Button type="primary" htmlType="submit" block loading={loginBusy}>登录并连接游戏服务</Button>
        </Form>
      </Modal>
    </div>
  )
}
