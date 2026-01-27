# 国标麻将数据库表设计

## guobiao_history_stats 国标麻将对局统计表

专门用于国标麻将（guobiao）规则的基础统计数据，按照 `rule`（规则，如 guobiao）与 `mode`（模式，如 `1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等）区分不同维度。客户端展示排行榜/统计时需与 `guobiao_fan_stats` 表关联查询。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL REFERENCES users(user_id) ON DELETE CASCADE | 用户 ID |
| rule | VARCHAR(10) | NOT NULL | 规则标识（guobiao） |
| mode | VARCHAR(20) | NOT NULL | 数据模式（`1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等） |
| total_games | INT | NOT NULL DEFAULT 0 | 总对局数 |
| total_rounds | INT | NOT NULL DEFAULT 0 | 累计回合数 |
| win_count | INT | NOT NULL DEFAULT 0 | 和牌次数 |
| self_draw_count | INT | NOT NULL DEFAULT 0 | 自摸次数 |
| deal_in_count | INT | NOT NULL DEFAULT 0 | 放铳次数 |
| total_fan_score | INT | NOT NULL DEFAULT 0 | 累计番数 |
| total_win_turn | INT | NOT NULL DEFAULT 0 | 累计和巡（用于计算平均巡目） |
| total_fangchong_score | INT | NOT NULL DEFAULT 0 | 累计放铳分 |
| first_place_count | INT | NOT NULL DEFAULT 0 | 一位次数 |
| second_place_count | INT | NOT NULL DEFAULT 0 | 二位次数 |
| third_place_count | INT | NOT NULL DEFAULT 0 | 三位次数 |
| fourth_place_count | INT | NOT NULL DEFAULT 0 | 四位次数 |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | 每个用户每个规则每个模式一条记录 |

## guobiao_fan_stats 国标麻将番种统计表

专门用于存储国标麻将的番种统计数据，按照 `rule`（规则，如 guobiao）与 `mode`（模式，如 `1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等）区分不同维度。

| 字段名 | 类型 | 约束 | 说明 |
|--------|------|------|------|
| user_id | BIGINT | NOT NULL REFERENCES users(user_id) ON DELETE CASCADE | 用户 ID |
| rule | VARCHAR(10) | NOT NULL | 规则标识（guobiao） |
| mode | VARCHAR(20) | NOT NULL | 数据模式（`1/4`、`2/4`、`3/4`、`4/4`、`1/4_rank` 等） |
| created_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 创建时间 |
| updated_at | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP | 最近更新时间 |
| PRIMARY KEY | (user_id, rule, mode) | 复合主键 | 每个用户每个规则每个模式一条记录 |

### 番种统计字段
以下字段全部位于 `guobiao_fan_stats` 中，每列都以 `INT NOT NULL DEFAULT 0` 记录对应番种累计出现次数：

| 字段名 | 说明 |
|--------|------|
| dasixi | 大四喜次数 |
| dasanyuan | 大三元次数 |
| lvyise | 绿一色次数 |
| jiulianbaodeng | 九莲宝灯次数 |
| sigang | 四杠次数 |
| sangang | 三杠次数 |
| lianqidui | 连七对次数 |
| shisanyao | 十三幺次数 |
| qingyaojiu | 清幺九次数 |
| xiaosixi | 小四喜次数 |
| xiaosanyuan | 小三元次数 |
| ziyise | 字一色次数 |
| sianke | 四暗刻次数 |
| yiseshuanglonghui | 一色双龙会次数 |
| yisesitongshun | 一色四同顺次数 |
| yisesijiegao | 一色四节高次数 |
| yisesibugao | 一色四步高次数 |
| hunyaojiu | 混幺九次数 |
| qiduizi | 七对子次数 |
| qixingbukao | 七星不靠次数 |
| quanshuangke | 全双刻次数 |
| qingyise | 清一色次数 |
| yisesantongshun | 一色三同顺次数 |
| yisesanjiegao | 一色三节高次数 |
| quanda | 全大次数 |
| quanzhong | 全中次数 |
| quanxiao | 全小次数 |
| qinglong | 清龙次数 |
| sanseshuanglonghui | 三色双龙会次数 |
| yisesanbugao | 一色三步高次数 |
| quandaiwu | 全带五次数 |
| santongke | 三同刻次数 |
| sananke | 三暗刻次数 |
| quanbukao | 全不靠次数 |
| zuhelong | 组合龙次数 |
| dayuwu | 大于五次数 |
| xiaoyuwu | 小于五次数 |
| sanfengke | 三风刻次数 |
| hualong | 花龙次数 |
| tuibudao | 推不倒次数 |
| sansesantongshun | 三色三同顺次数 |
| sansesanjiegao | 三色三节高次数 |
| wufanhe | 无番和次数 |
| miaoshouhuichun | 妙手回春次数 |
| haidilaoyue | 海底捞月次数 |
| gangshangkaihua | 杠上开花次数 |
| qiangganghe | 抢杠和次数 |
| pengpenghe | 碰碰和次数 |
| hunyise | 混一色次数 |
| sansesanbugao | 三色三步高次数 |
| wumenqi | 五门齐次数 |
| quanqiuren | 全求人次数 |
| shuangangang | 双暗杠次数 |
| shuangjianke | 双箭刻次数 |
| quandaiyao | 全带幺次数 |
| buqiuren | 不求人次数 |
| shuangminggang | 双明杠次数 |
| hejuezhang | 和绝张次数 |
| jianke | 箭刻次数 |
| quanfengke | 圈风刻次数 |
| menfengke | 门风刻次数 |
| menqianqing | 门前清次数 |
| pinghe | 平和次数 |
| siguiyi | 四归一次数 |
| shuangtongke | 双同刻次数 |
| shuanganke | 双暗刻次数 |
| angang | 暗杠次数 |
| duanyao | 断幺次数 |
| yibangao | 一般高次数 |
| xixiangfeng | 喜相逢次数 |
| lianliu | 连六次数 |
| laoshaofu | 老少副次数 |
| yaojiuke | 幺九刻次数 |
| minggang | 明杠次数 |
| queyimen | 缺一门次数 |
| wuzi | 无字次数 |
| bianzhang | 边张次数 |
| qianzhang | 嵌张次数 |
| dandiaojiang | 单钓将次数 |
| zimo | 自摸次数 |
| huapai | 花牌次数 |
| mingangang | 明暗杠次数 |

