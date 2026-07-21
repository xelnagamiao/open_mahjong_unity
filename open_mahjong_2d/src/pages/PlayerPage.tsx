import { useEffect, useState } from 'react'
import { ArrowLeftOutlined, IdcardOutlined } from '@ant-design/icons'
import { Button, Card, Descriptions, Empty, Progress, Spin, Statistic, message } from 'antd'
import { useNavigate, useParams } from 'react-router-dom'
import { playerProfileUrl, publicApiGet } from '../salasasa/api'
import type { PublicPlayerInfo } from '../salasasa/types'
import './PlayerPage.css'

type ProfilePayload = PublicPlayerInfo & {
  user_settings?: { username?: string; title_id?: number; profile_image_id?: number }
  rank?: { guobiao_rank?: string; guobiao_score?: number; progress?: number | { percent?: number } }
  guobiao_stats?: Array<Record<string, unknown>>
}

export default function PlayerPage() {
  const navigate = useNavigate()
  const { key = '' } = useParams()
  const [profile, setProfile] = useState<ProfilePayload | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    publicApiGet<ProfilePayload>(playerProfileUrl(key))
      .then(setProfile)
      .catch((error) => message.error(error instanceof Error ? error.message : '玩家资料加载失败'))
      .finally(() => setLoading(false))
  }, [key])

  if (loading) return <div className="profile-loading"><Spin size="large" /></div>
  if (!profile) return <div className="profile-loading"><Empty description="未找到玩家"><Button onClick={() => navigate('/')}>返回大厅</Button></Empty></div>

  const username = profile.user_settings?.username ?? profile.username ?? `玩家 ${profile.user_id}`
  const rank = profile.rank?.guobiao_rank ?? profile.guobiao_rank ?? '未定级'
  const score = profile.rank?.guobiao_score ?? profile.guobiao_score ?? 0
  const rawProgress = profile.rank?.progress
  const progress = typeof rawProgress === 'number' ? rawProgress : rawProgress?.percent

  return (
    <div className="profile-page">
      <div className="profile-wrap">
        <Button icon={<ArrowLeftOutlined />} onClick={() => navigate('/')}>返回匹配大厅</Button>
        <Card className="profile-identity">
          <div className="profile-avatar"><IdcardOutlined /></div>
          <div><span>Salasasa 玩家资料</span><h1>{username}</h1><p>用户 ID：{profile.user_id}</p></div>
        </Card>
        <div className="profile-grid">
          <Card><Statistic title="国标段位" value={rank} /></Card>
          <Card><Statistic title="国标分数" value={score} precision={1} /></Card>
          <Card title="段位进度"><Progress percent={Math.max(0, Math.min(100, Number(progress ?? 0)))} /></Card>
        </div>
        <Card title="公开资料">
          <Descriptions column={{ xs: 1, sm: 2 }}>
            <Descriptions.Item label="用户名">{username}</Descriptions.Item>
            <Descriptions.Item label="用户 ID">{profile.user_id}</Descriptions.Item>
            <Descriptions.Item label="国标段位">{rank}</Descriptions.Item>
            <Descriptions.Item label="国标分数">{score}</Descriptions.Item>
          </Descriptions>
        </Card>
      </div>
    </div>
  )
}
