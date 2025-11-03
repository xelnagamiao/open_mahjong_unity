import os
import subprocess
import secrets
import hashlib
import asyncio
"""
聊天服务器类用于生成一份本地哈希，并且启动一个基于golang的聊天服务器，
在用户登录时发送根据本地哈希加密的秘钥给客户端，客户端使用此哈希值在聊天服务器中验证，即可使用聊天功能。
"""
class ChatServer:  
    async def start_chat_server(self):
        
        # 生成密钥
        global HASH_SALT
        HASH_SALT = secrets.token_urlsafe(16)
        
        # 保存秘钥到文件
        await self.save_secret_key_to_file(HASH_SALT)
        await asyncio.sleep(2)
        
        # 验证文件是否写入成功
        script_dir = os.path.dirname(os.path.abspath(__file__))
        secret_file_path = os.path.join(script_dir, 'secret_key.txt')
        
        # 如果不存在则报错
        if not os.path.exists(secret_file_path):
            print(f"秘钥文件不存在: {secret_file_path}")
            return
            
        # 验证文件内容是否有效
        try:
            with open(secret_file_path, 'r') as f:
                saved_key = f.read().strip()
                if not saved_key or len(saved_key) < 10:
                    print(f"秘钥文件内容无效: {saved_key}")
                    return
                print(f"秘钥文件验证成功: {saved_key[:20]}...")
        except Exception as e:
            print(f"读取秘钥文件失败: {e}")
            return
        
        # 聊天服务器可执行文件路径
        executable_path = os.path.join(script_dir, 'open_mahjong_chatServer.exe')
        
        try:
            # 检查可执行文件是否存在
            if not os.path.exists(executable_path):
                print(f"聊天服务器可执行文件不存在: {executable_path}")
                return
                
            # 调试环境使用命令行窗口显示日志信息
            process = subprocess.Popen(
            ['cmd.exe', '/k', 'cd /d', script_dir, '&&', executable_path],
            creationflags=subprocess.CREATE_NEW_CONSOLE,
            )
            """
            部署环境使用stdout/stderr捕获，避免日志输出到控制台
            process = subprocess.Popen(
                [executable_path],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True
            )
            """
            print(f"聊天服务器进程已启动，PID: {process.pid}")
            
        except Exception as e:
            print(f"启动聊天服务器失败: {e}")
            print(f"错误详情: {str(e)}")

    # 将秘钥保存到文件，供聊天服务器读取
    async def save_secret_key_to_file(self, secret_key: str):
        """将秘钥保存到文件，供聊天服务器读取"""
        try:
            # 获取当前脚本所在目录
            self_dir = os.path.dirname(os.path.abspath(__file__))
            # 秘钥文件直接保存在根目录
            secret_file_path = os.path.join(self_dir, 'secret_key.txt')
            
            # 使用临时文件确保原子性写入
            temp_file_path = secret_file_path + '.tmp'
            
            # 写入临时文件
            with open(temp_file_path, 'w', encoding='utf-8') as f:
                f.write(secret_key)
                f.flush()  # 强制刷新缓冲区
                os.fsync(f.fileno())  # 强制写入磁盘
            
            # 如果原文件存在，先删除
            if os.path.exists(secret_file_path):
                os.remove(secret_file_path)
                
            # 原子性重命名
            os.rename(temp_file_path, secret_file_path)
            
            # 验证文件写入是否成功
            with open(secret_file_path, 'r', encoding='utf-8') as f:
                saved_content = f.read().strip()
                if saved_content != secret_key:
                    raise Exception(f"文件内容验证失败: 期望 {secret_key}, 实际 {saved_content}")
            
            print(f"新秘钥已保存到 {secret_file_path}")
            print(f"秘钥: {secret_key[:20]}...")
            print(f"文件大小: {os.path.getsize(secret_file_path)} 字节")

        except Exception as e:
            print(f"保存秘钥失败: {e}")
            # 清理临时文件
            temp_file_path = os.path.join(self_dir, 'secret_key.txt.tmp')
            if os.path.exists(temp_file_path):
                try:
                    os.remove(temp_file_path)
                except:
                    pass
            
    # 哈希用户名
    async def hash_username(self, username: str) -> str:
        return hashlib.sha256((username + HASH_SALT).encode()).hexdigest()