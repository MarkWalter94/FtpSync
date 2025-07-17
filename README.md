Setup FtpSync is easy:

1. Configure the `appsettings.json` file, configuring the files section, `FilesToUploadFolder` is the source path in local full path form, TargetFolderFtp is the path on the ftp server.
```json
  "Files": [
    {
      "FilesToUploadFolder": "Path\\To\\First\\Folder",
      "TargetFolderFtp": "Path\\To\\First\\Ftp\\Folder"
    },
    {
    "FilesToUploadFolder": "Path\\To\\Second\\Folder",
    "TargetFolderFtp": "Path\\To\\Second\\Ftp\\Folder"
    }
  ],
  "Database": {
    "Path": "ftpsync.db"
  },
  "Settings": {
    "MaxRetries": 10,
    "DeleteAfterTransfer": false,
    "LoopSeconds": 120
  }
}
```

2. Set the authorization parameters once: We don't want to store the secure data in the appsettings.json file, for obvius reasons. So we insert them once at the first startup, then, we will encrypt them with a random key in the AES256 format and save the password files locally.
We will launch the program from command line like this:
```batch
Location\To\FtpSync.exe --server ftp.youserver.com --user paolo --password paolo84 
```
This will launch the program, in the first run this will create the database and save the sensitive informations cripted into it.

3. Launch the program and let it sync, or install it as a windows service.