# النشر الآمن للإنتاج

يعتمد الإنتاج إصداراً اختبره GitHub Actions، ولا يسحب الفرع دورياً من الخادم.

## المسار المعتمد

1. يدفع التعديل إلى فرع `online-development`.
2. يشغّل سير العمل `Full test suite` جميع اختبارات .NET وE2E.
3. عند نجاح الاختبارات فقط، يشغّل `Deploy tested release to Hostinger VPS` على رقم الالتزام نفسه.
4. ينشئ الخادم نسخة PostgreSQL متحققاً منها قبل استبدال التطبيق.
5. يشغّل الإصدار الجديد ويفحص `/healthz` محلياً وعبر HTTPS.
6. إذا أخفق البناء أو فحص الجاهزية، يعيد الخادم ملفات الإصدار السابق تلقائياً.

يحافظ النشر على اسم مشروع Docker الحالي `repository` صراحةً، حتى تبقى وحدات PostgreSQL والتخزين وCaddy نفسها عند الانتقال من مجلد المستودع القديم إلى مجلد الإصدارات الجديد.

## الانتقال من المؤقت القديم

قبل أول نشر عبر GitHub Actions يجب تنفيذ الآتي مرة واحدة على الخادم:

```bash
sudo systemctl disable --now tasareehapp-deploy.timer
sudo systemctl reset-failed tasareehapp-deploy.service || true
```

ملف `ops/pull-deploy.sh` متروك كأمر آمن لا ينشر شيئاً، حتى لو أعيد تشغيل الوحدة القديمة بالخطأ.

## أسرار GitHub المطلوبة

- `VPS_HOST`
- `VPS_USER`
- `VPS_SSH_KEY`
- `VPS_KNOWN_HOSTS`

تُحفظ الأسرار في GitHub Environment باسم `production`، ويفضل تفعيل موافقة يدوية للبيئة عند بدء التشغيل التجاري.

## فحوص الصحة

- `/healthz/live`: يثبت أن عملية التطبيق تعمل.
- `/healthz`: يثبت أن التطبيق يعمل ويستطيع الاتصال بقاعدة PostgreSQL؛ يعيد `503` دون تفاصيل داخلية عند التعذر.

## حدود الرجوع الآلي

الرجوع الآلي يعيد ملفات التطبيق والحاويات، لكنه لا يعكس ترحيلات قاعدة البيانات. لذلك يجب أن تكون ترحيلات الإنتاج متوافقة مع الإصدار السابق، وتنفذ التغييرات الكاسرة في مرحلتين. نسخة ما قبل النشر تبقى شبكة الأمان لاستعادة البيانات يدوياً عند الضرورة.

## النشر اليدوي

يمكن تشغيل سير العمل يدوياً من GitHub. حتى في هذه الحالة يشغّل الاختبارات الكاملة أولاً، ولا يبدأ النشر إذا أخفقت.

## النسخ المشفر خارج الخادم

يستخدم المسار `restic`، ولذلك تكون النسخ مشفرة قبل إرسالها إلى التخزين الخارجي. ثبّت `restic` على الخادم، ثم أنشئ ملف `/opt/tasareehapp/offsite-backup.env` بصلاحية `600`:

```bash
RESTIC_REPOSITORY=s3:https://s3.example.com/tasareehapp-production
RESTIC_PASSWORD_FILE=/opt/tasareehapp/secrets/restic-password
AWS_ACCESS_KEY_ID=replace-me
AWS_SECRET_ACCESS_KEY=replace-me
RESTIC_KEEP_DAILY=14
RESTIC_KEEP_WEEKLY=8
RESTIC_KEEP_MONTHLY=12
```

لا يُحفظ هذا الملف ولا ملف كلمة مرور `restic` في Git. عند وجود الإعدادات، يفعّل النشر تلقائياً:

- نسخة مشفرة يومية الساعة 03:45 بتوقيت الخادم.
- تحقق أسبوعي من المستودع، واستعادة أحدث Dump، وفحص checksum، ثم تمريره إلى `pg_restore --list`.
- كتابة وقت آخر استعادة ناجحة في `/opt/tasareehapp/last-restore-check`.

للتشغيل اليدوي:

```bash
sudo systemctl start tasareehapp-offsite-backup.service
sudo systemctl start tasareehapp-restore-check.service
sudo journalctl -u tasareehapp-restore-check.service -n 100 --no-pager
```

## مراقبة الإنتاج

يثبّت النشر مؤقت `tasareehapp-monitor.timer` ليعمل كل خمس دقائق. يفحص:

- جاهزية التطبيق واتصاله بقاعدة البيانات.
- صحة حاويتي PostgreSQL وClamAV.
- نسبة استخدام قرص الخادم.
- عمر أحدث نسخة PostgreSQL محلية.
- عدد أجهزة البوابات المعتمدة التي انقطع Heartbeat عنها.
- أخطاء النظام المسجلة خلال آخر 24 ساعة.

تُكتب آخر نتيجة في `/opt/tasareehapp/monitoring-status.json` وتظهر الإخفاقات في `journalctl`. ولإرسال تنبيه فوري، أنشئ ملف `/opt/tasareehapp/monitoring.env` بصلاحية `600`:

```bash
MONITORING_WEBHOOK_URL=https://alerts.example.com/tasareehapp
MONITORING_MAX_DISK_PERCENT=85
MONITORING_MAX_BACKUP_AGE_HOURS=30
MONITORING_GATE_OFFLINE_MINUTES=3
```

وللفحص اليدوي:

```bash
sudo systemctl start tasareehapp-monitor.service
sudo cat /opt/tasareehapp/monitoring-status.json
sudo journalctl -u tasareehapp-monitor.service -n 100 --no-pager
```
