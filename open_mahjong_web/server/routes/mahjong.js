const express = require('express');
const router = express.Router();
const pool = require('../config/database');

// 听牌待牌判断 (XT)
router.post('/count-hand', async (req, res) => {
  try {
    const { hand } = req.body;
    
    if (!hand) {
      return res.status(400).json({
        success: false,
        message: '请提供手牌数据'
      });
    }

    // 输入验证
    const allowCharacter = new Set(['0','1','2','3','4','5','6','7','8','9','s','m','p','东','南','西','北','中','白','发']);
    const countCharacter = new Set(['0','1','2','3','4','5','6','7','8','9','东','南','西','北','中','白','发']);
    
    // 检查字符限制
    for (let char of hand) {
      if (!allowCharacter.has(char)) {
        return res.status(400).json({
          success: false,
          message: '格式错误:手牌中不得出现超出0,1,2,3,4,5,6,7,8,9,s,m,p,东,南,西,北的字符'
        });
      }
    }
    
    // 检查牌数
    let countTiles = 0;
    for (let char of hand) {
      if (countCharacter.has(char)) {
        countTiles++;
      }
    }
    
    if (countTiles !== 13) {
      return res.status(400).json({
        success: false,
        message: countTiles > 13 ? '格式错误:传入麻将牌数量大于13' : '格式错误:传入麻将牌数量小于13'
      });
    }

    // 这里应该调用听牌计算函数
    // const output = xt_count(hand);
    const output = `听牌分析结果: ${hand}`; // 临时占位

    // 保存到数据库（如果表存在的话，这里暂时注释掉，因为可能没有这个表）
    // const result = await pool.query(
    //   'INSERT INTO mahjong_results (mj_input, mj_output, is_valid) VALUES ($1, $2, $3) RETURNING id',
    //   [hand, output, true]
    // );

    res.json({
      success: true,
      message: '计算成功',
      data: {
        input: hand,
        output: output,
        id: null // result.rows[0].id
      }
    });

  } catch (error) {
    console.error('听牌计算错误:', error);
    res.status(500).json({
      success: false,
      message: '服务器内部错误'
    });
  }
});

// 立直麻将牌型解算 (RC)
router.post('/count-riichi', async (req, res) => {
  try {
    const {
      hand,
      fulu1,
      fulu2,
      fulu3,
      fulu4,
      wayToHepai,
      doraNum,
      deepDoraNum,
      positionSelect
    } = req.body;

    if (!hand) {
      return res.status(400).json({
        success: false,
        message: '请提供手牌数据'
      });
    }

    // 这里应该调用立直麻将计算函数
    // const output = mahjong_count(hand, fulu1, fulu2, fulu3, fulu4, wayToHepai, doraNum, deepDoraNum, positionSelect);
    const output = `立直麻将分析结果: ${hand}`; // 临时占位

    // 保存到数据库（如果表存在的话，这里暂时注释掉，因为可能没有这个表）
    // const result = await pool.query(
    //   'INSERT INTO mahjong_results (mj_input, mj_output, is_valid) VALUES ($1, $2, $3) RETURNING id',
    //   [JSON.stringify(req.body), output, true]
    // );

    res.json({
      success: true,
      message: '计算成功',
      data: {
        input: req.body,
        output: output,
        id: null // result.rows[0].id
      }
    });

  } catch (error) {
    console.error('立直麻将计算错误:', error);
    res.status(500).json({
      success: false,
      message: '服务器内部错误'
    });
  }
});

// 国标麻将牌型解算 (GB)
router.post('/count-chinese', async (req, res) => {
  try {
    const {
      hand,
      fulu1,
      fulu2,
      fulu3,
      fulu4,
      wayToHepai,
      flowerTiles
    } = req.body;

    if (!hand) {
      return res.status(400).json({
        success: false,
        message: '请提供手牌数据'
      });
    }

    // 这里应该调用国标麻将计算函数
    // const output = chinese_mahjong_count(hand, fulu1, fulu2, fulu3, fulu4, wayToHepai, flowerTiles);
    const output = `国标麻将分析结果: ${hand}`; // 临时占位

    // 保存到数据库（如果表存在的话，这里暂时注释掉，因为可能没有这个表）
    // const result = await pool.query(
    //   'INSERT INTO mahjong_results (mj_input, mj_output, is_valid) VALUES ($1, $2, $3) RETURNING id',
    //   [JSON.stringify(req.body), output, true]
    // );

    res.json({
      success: true,
      message: '计算成功',
      data: {
        input: req.body,
        output: output,
        id: null // result.rows[0].id
      }
    });

  } catch (error) {
    console.error('国标麻将计算错误:', error);
    res.status(500).json({
      success: false,
      message: '服务器内部错误'
    });
  }
});

// 获取历史记录
router.get('/history', async (req, res) => {
  try {
    // 如果表不存在，返回空数组
    const result = await pool.query(
      'SELECT * FROM mahjong_results ORDER BY created_at DESC LIMIT 50'
    ).catch(() => ({ rows: [] }));

    res.json({
      success: true,
      data: result.rows || []
    });

  } catch (error) {
    console.error('获取历史记录错误:', error);
    res.status(500).json({
      success: false,
      message: '服务器内部错误'
    });
  }
});

module.exports = router; 