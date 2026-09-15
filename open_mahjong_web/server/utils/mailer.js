const nodemailer = require('nodemailer');
const config = require('../config/config');

let transporter = null;

function getTransporter() {
  if (transporter) return transporter;
  const smtp = config.smtp;
  if (!smtp?.enabled) return null;
  transporter = nodemailer.createTransport({
    host: smtp.host,
    port: smtp.port,
    secure: smtp.secure,
    auth: {
      user: smtp.user,
      pass: smtp.pass,
    },
  });
  return transporter;
}

async function sendMail({ to, subject, text, html }) {
  const smtp = config.smtp;
  if (!smtp?.enabled) {
    const err = new Error('邮件服务未配置');
    err.code = 'SMTP_DISABLED';
    throw err;
  }
  const tx = getTransporter();
  await tx.sendMail({
    from: `"${smtp.fromName}" <${smtp.fromEmail}>`,
    to,
    subject,
    text,
    html,
  });
}

async function sendEmailBindCode({ to, code, username }) {
  const subject = '【salasasa】邮箱绑定验证码';
  const text = [
    `${username || '玩家'} 您好，`,
    '',
    `您正在绑定 salasasa.cn 账户邮箱，验证码为：${code}`,
    '验证码 10 分钟内有效。如非本人操作，请忽略本邮件。',
    '',
    '— salasasa.cn',
  ].join('\n');
  const html = `
    <p>${username || '玩家'} 您好，</p>
    <p>您正在绑定 <strong>salasasa.cn</strong> 账户邮箱，验证码为：</p>
    <p style="font-size:24px;font-weight:700;letter-spacing:4px;">${code}</p>
    <p>验证码 10 分钟内有效。如非本人操作，请忽略本邮件。</p>
    <p style="color:#888;">— salasasa.cn</p>
  `;
  await sendMail({ to, subject, text, html });
}

async function sendPasswordResetCode({ to, code }) {
  await sendMail({
    to,
    subject: '【salasasa】重置密码验证码',
    text: `您正在重置 salasasa.cn 账户密码，验证码为：${code}。验证码 10 分钟内有效，请勿向他人提供。如非本人操作，请忽略本邮件。`,
    html: `<p>您正在重置 <strong>salasasa.cn</strong> 账户密码，验证码为：</p>
      <p style="font-size:24px;font-weight:700;letter-spacing:4px;">${code}</p>
      <p>验证码 10 分钟内有效，请勿向他人提供。如非本人操作，请忽略本邮件。</p>`,
  });
}

module.exports = { sendMail, sendEmailBindCode, sendPasswordResetCode };
