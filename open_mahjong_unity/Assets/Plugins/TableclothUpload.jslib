// TableclothUpload.jslib
// Unity WebGL 插件：用于上传桌布图片文件

var TableclothUploadPlugin = {
    // 上传桌布图片文件
    // gameObjectNamePtr: Unity GameObject 名称，用于回调
    // methodNamePtr: Unity 回调方法名称
    // filterPtr: 文件过滤器，例如 "image/png,image/jpeg,image/jpg"
    UploadFileJS: function(gameObjectNamePtr, methodNamePtr, filterPtr) {
        var gameObjectName = UTF8ToString(gameObjectNamePtr);
        var methodName = UTF8ToString(methodNamePtr);
        var filter = UTF8ToString(filterPtr);

        // 删除已存在的文件输入元素
        var fileInput = document.getElementById(gameObjectName);
        if (fileInput) {
            document.body.removeChild(fileInput);
        }

        // 创建文件输入元素
        fileInput = document.createElement('input');
        fileInput.setAttribute('id', gameObjectName);
        fileInput.setAttribute('type', 'file');
        fileInput.setAttribute('style', 'display:none;');
        fileInput.setAttribute('accept', filter || 'image/png,image/jpeg,image/jpg');

        // 文件选择回调
        fileInput.onchange = function(event) {
            var file = event.target.files[0];
            if (!file) {
                // 用户取消选择
                SendMessage(gameObjectName, methodName, '');
                document.body.removeChild(fileInput);
                return;
            }

            // 使用 FileReader 读取文件为 base64
            var reader = new FileReader();
            reader.onload = function(e) {
                // base64 数据格式：data:image/png;base64,xxxxx
                var base64Data = e.target.result;
                // 提取文件名和扩展名
                var fileName = file.name;
                var fileExtension = fileName.substring(fileName.lastIndexOf('.'));
                
                // 将数据传递给 Unity：格式为 "base64Data|fileName|fileExtension"
                var dataToSend = base64Data + '|' + fileName + '|' + fileExtension;
                SendMessage(gameObjectName, methodName, dataToSend);
                
                // 清理
                document.body.removeChild(fileInput);
            };
            reader.onerror = function() {
                SendMessage(gameObjectName, methodName, '');
                document.body.removeChild(fileInput);
            };
            
            // 读取文件为 Data URL (base64)
            reader.readAsDataURL(file);
        };

        // 添加到 DOM 并触发点击
        document.body.appendChild(fileInput);
        
        // 延迟触发点击，确保元素已添加到 DOM
        setTimeout(function() {
            fileInput.click();
        }, 0);
    }
};

mergeInto(LibraryManager.library, TableclothUploadPlugin);

