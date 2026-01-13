#!/usr/bin/env python
"""
麻将服务器启动脚本
直接运行: python main.py
"""
import uvicorn

if __name__ == "__main__":
    uvicorn.run(
        "server.server:app",
        host="localhost",
        port=8081,
        reload=False
    )