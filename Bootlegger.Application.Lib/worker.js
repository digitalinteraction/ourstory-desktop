﻿module.exports = {
    LOCALONLY: true,
    hostname: 'localhost',
    db_host: 'mongo',
    db_port: 27017,
    db_user: '',
    db_password: '',
    db_database: 'bootlegger',
    S3_URL: 'http://' + process.env.MYIP + ':3000/',
    S3_CLOUD_URL: 'http://' + process.env.MYIP + ':3000/upload/',
    S3_TRANSCODE_URL: 'http://' + process.env.MYIP + ':3000/transcode/upload/',
    BEANSTALK_HOST: 'beanstalk',
    BEANSTALK_PORT: '11300',
    master_url: 'http://web',
    master_url_port: '1337',
    CURRENT_EDIT_KEY: '8ee208fa-a20d-4cf5-9a12-6803c8a4852f',
    MIN_CLIP_COUNT: 1,
    MAX_CACHE: 10 * 1024 * 1024 * 1024
}