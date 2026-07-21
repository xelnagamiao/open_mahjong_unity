import { useEffect, useRef, useState } from 'react'
import { Button, Card, Modal, Slider, Space, Spin, Table, Tag, message } from 'antd'
import { ArrowLeftOutlined, SettingOutlined, WifiOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { MahjongScene } from '../game/scene/MahjongScene'
import { loadStoredVolume, saveStoredVolume } from '../lib/storage'
import { useStoredSceneAppearance } from '../lib/useStoredSceneAppearance'
import { salasasaClient } from '../salasasa/client'
import { SalasasaGameAdapter } from '../salasasa/gameAdapter'
import { useSession } from '../salasasa/SessionContext'
import type { SalasasaGameEndInfo, SalasasaResponse, SalasasaResultInfo } from '../salasasa/types'
import './GamePage.css'

const SOUND_ALIASES = [
  '01-start', '03-cd', '05-draw', '06-discard', '08-inquire', '09-cpk',
  '14-chow-m', '16-pung-m', '18-kong-m', '20-win-m', '25-xchg',
]

export default function GamePage() {
  const navigate = useNavigate()
  const { player, status, restoring } = useSession()
  const stageRef = useRef<HTMLDivElement | null>(null)
  const sceneRef = useRef<MahjongScene | null>(null)
  const adapterRef = useRef<SalasasaGameAdapter | null>(null)
  const [sceneReady, setSceneReady] = useState(false)
  const [hasSnapshot, setHasSnapshot] = useState(false)
  const [roundResult, setRoundResult] = useState<SalasasaResultInfo | null>(null)
  const [readySent, setReadySent] = useState(false)
  const [finalResult, setFinalResult] = useState<SalasasaGameEndInfo | null>(null)
  const [settingsOpen, setSettingsOpen] = useState(false)
  const [volume, setVolume] = useState(() => loadStoredVolume())
  const { appearance, backgroundImage } = useStoredSceneAppearance()

  useEffect(() => {
    if (!player || !stageRef.current) return
    let cancelled = false
    const adapter = new SalasasaGameAdapter(player.user_id)
    adapterRef.current = adapter
    const scene = new MahjongScene((type, payload) => {
      if (type === 'ping') return
      if (type !== 'game.input') return
      const outgoing = adapter.encodeSceneInput(payload)
      if (!outgoing || !salasasaClient.send(outgoing)) message.error('操作未发送，请等待连接恢复')
    })
    sceneRef.current = scene
    void scene.mount(stageRef.current).then((mounted) => {
      if (!mounted || cancelled) return
      scene.setVolume(volume)
      scene.setAppearance(appearance)
      scene.setBackgroundImage(backgroundImage?.dataUrl ?? null)
      for (const alias of SOUND_ALIASES) {
        const audio = new Audio(`${import.meta.env.BASE_URL}sounds/${alias}.wav`)
        audio.preload = 'auto'
        scene.loadSound(alias, audio)
      }
      setSceneReady(true)
      const cached = salasasaClient.lastGameStart
      if (cached) applyMessage(cached, adapter, scene)
    })
    return () => {
      cancelled = true
      setSceneReady(false)
      scene.destroy()
      if (sceneRef.current === scene) sceneRef.current = null
      if (adapterRef.current === adapter) adapterRef.current = null
    }
    // Scene is owned by this player for the lifetime of the page.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [player?.user_id])

  function applyMessage(response: SalasasaResponse, adapter = adapterRef.current, scene = sceneRef.current) {
    if (!adapter || !scene) return
    try {
      const update = adapter.accept(response)
      if (!update) return
      if (update.snapshot) {
        scene.flushFromSnapshot(update.snapshot)
        setHasSnapshot(true)
        setRoundResult(null)
        setReadySent(false)
      }
      if (update.event) scene.handleEvent(update.event)
      if (update.events) for (const event of update.events) scene.handleEvent(event)
      if (update.result) {
        setRoundResult(update.result)
        setReadySent(false)
      }
      if (update.ended) setFinalResult(update.ended)
    } catch (error) {
      message.error(error instanceof Error ? error.message : '2D 牌桌数据转换失败')
    }
  }

  useEffect(() => salasasaClient.subscribe((response) => {
    if (response.type.startsWith('gamestate/guobiao/')) applyMessage(response)
    if (response.type === 'tips' && response.message) message.info(response.message)
  }), [])

  useEffect(() => { sceneRef.current?.setAppearance(appearance) }, [appearance])
  useEffect(() => { sceneRef.current?.setBackgroundImage(backgroundImage?.dataUrl ?? null) }, [backgroundImage?.dataUrl])

  const changeVolume = (next: number) => {
    setVolume(next)
    saveStoredVolume(next)
    sceneRef.current?.setVolume(next)
  }

  const sendReady = () => {
    const outgoing = adapterRef.current?.readyMessage()
    if (!outgoing || !salasasaClient.send(outgoing)) {
      message.error('暂时无法发送准备状态')
      return
    }
    setReadySent(true)
  }

  if (!player && !restoring) {
    return <div className="game-blocked"><Card className="game-blocked-card"><h1>需要先登录</h1><p>登录后才能恢复或进入国标对局。</p><Button type="primary" onClick={() => navigate('/')}>返回大厅</Button></Card></div>
  }

  const finalRows = Object.entries(finalResult?.player_final_data ?? {})
    .map(([seat, value]) => ({ seat, ...value }))
    .sort((a, b) => (a.rank ?? 99) - (b.rank ?? 99))

  return (
    <div className="mahjongGame" style={{ background: appearance.backgroundColorOutside }}>
      <div className="game-page__stage-shell game-page__stage-shell--full">
        <div ref={stageRef} className="game-stage" />
        {(!sceneReady || !hasSnapshot) && <div className="game-loading"><Spin size="large" /><span>{sceneReady ? '等待服务端恢复国标牌局…' : '正在加载 2D 牌桌…'}</span></div>}
        <div className="game-toolbar">
          <Tag icon={<WifiOutlined />} color={status === 'online' ? 'green' : 'orange'}>{status === 'online' ? '已连接' : '重连中'}</Tag>
          <Button icon={<SettingOutlined />} onClick={() => setSettingsOpen((value) => !value)}>设置</Button>
          <Button icon={<ArrowLeftOutlined />} disabled={!finalResult} onClick={() => navigate('/')}>返回大厅</Button>
        </div>
        {settingsOpen && <Card className="game-settings" title="牌桌设置" size="small"><span>音量</span><Slider min={0} max={1} step={0.05} value={volume} onChange={changeVolume} /></Card>}
      </div>

      <Modal
        title={roundResult?.hepai_player_index == null ? '本局流局' : `第 ${Number(roundResult?.hepai_player_index) + 1} 家和牌`}
        open={Boolean(roundResult) && !finalResult}
        closable={false}
        maskClosable={false}
        footer={<Button type="primary" loading={readySent} disabled={readySent} onClick={sendReady}>{readySent ? '已准备，等待其他玩家' : '准备下一局'}</Button>}
      >
        <Space direction="vertical" size="middle" style={{ width: '100%' }}>
          <div className="round-score"><strong>{roundResult?.hu_score ?? 0} 番</strong><span>{roundResult?.hu_class || '流局'}</span></div>
          <div className="fan-list">{roundResult?.hu_fan?.length ? roundResult.hu_fan.map((fan) => <Tag key={fan} color="blue">{fan}</Tag>) : <span>无番种信息</span>}</div>
          {roundResult?.score_changes && <div className="score-changes">{Object.entries(roundResult.score_changes).map(([seat, score]) => <span key={seat}>座位 {Number(seat) + 1}：{score >= 0 ? '+' : ''}{score}</span>)}</div>}
        </Space>
      </Modal>

      <Modal title="国标排位结束" open={Boolean(finalResult)} closable={false} maskClosable={false} footer={<Button type="primary" onClick={() => navigate('/')}>返回匹配大厅</Button>} width={620}>
        <Table rowKey="seat" pagination={false} dataSource={finalRows} columns={[
          { title: '名次', dataIndex: 'rank', width: 70, render: (value: number) => `第 ${value} 名` },
          { title: '玩家', dataIndex: 'username' },
          { title: '总分', dataIndex: 'score', width: 90 },
          { title: '段位变化', width: 150, render: (_, row) => `${row.rank_before ?? '—'} → ${row.rank_after ?? '—'}` },
        ]} />
      </Modal>
    </div>
  )
}
