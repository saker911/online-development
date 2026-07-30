# إعداد PostgreSQL لنسخة الأون لاين

## وضع التطوير الحالي

يبقى تشغيل المشروع المحلي على المنفذ `5001` باستخدام SQLite وقاعدة مستقلة داخل `.online-storage`. لا يلزم تثبيت PostgreSQL لمواصلة تطوير الواجهات والوظائف.

## تفعيل PostgreSQL

اضبط القيم التالية كمتغيرات بيئة في الخادم أو خدمة التشغيل:

```text
Data__Provider=PostgreSql
ConnectionStrings__PostgreSqlConnection=Host=127.0.0.1;Port=5432;Database=vehicle_permit_org;Username=vehicle_permit_app;Password=<secret>
```

لا تضع كلمة المرور الحقيقية في `appsettings.json` أو في GitHub.

عند بدء التطبيق على قاعدة جديدة، ينفذ النظام EF Core Migrations تلقائيًا ثم ينشئ إعدادات النظام الأولية. قاعدة SQLite المحلية لا تتأثر.

## عزل بيانات الجهات

البنية الحالية تستخدم قاعدة PostgreSQL مشتركة مع عزل منطقي للجهات. تحمل السجلات التشغيلية قيمة `TenantId`، ويطبق `ApplicationDbContext` مرشحات الجهة تلقائيًا حسب رابط الجهة والجلسة الموثقة.

- لكل جهة معرف ورابط مختصر داخل دومين المنصة مثل `/o/company-name`.
- المستخدم مرتبط بجهة واحدة وتنتقل هوية الجهة داخل جلسة الدخول.
- العمليات الإدارية العابرة للجهات محصورة في حساب مدير المنصة.
- الباقة وحالة الاشتراك وحدود الاستخدام محفوظة على سجل الجهة.

يمكن تقديم قاعدة مخصصة كخيار مؤسسي لاحقًا، لكنه يحتاج سلسلة اتصال وتشغيل ونسخًا احتياطيًا مستقلًا لكل جهة، وليس هذا هو وضع التشغيل الحالي.

## ترتيب دومين المنصة والاستضافة

1. وجّه سجل `A` أو `CNAME` للدومين إلى خادم التطبيق أو موازن الحمل.
2. شغّل التطبيق داخليًا على منفذ مثل `5001`، ولا تعرضه مباشرة للإنترنت.
3. استخدم Nginx أو Caddy أو IIS كوكيل عكسي أمام التطبيق.
4. فعّل شهادة SSL واجعل التحويل من HTTP إلى HTTPS دائمًا.
5. أرسل ترويسات `X-Forwarded-For` و`X-Forwarded-Proto` و`Host` إلى التطبيق.
6. أضف عنوان الوكيل إلى `ForwardedHeaders__KnownProxies` حتى ينشئ التطبيق روابط HTTPS صحيحة.
7. اضبط `AllowedHosts` على دومينات المنصة الفعلية بدل `*`.
8. اختبر رابط جهة مثل `https://app.example.com/o/company-name`.

تستخدم جميع الجهات دومين المنصة نفسه، ويحصل كل عميل على رابط بالصيغة `/o/{slug}`. لا يحتاج العميل إلى DNS أو شهادة منفصلة، وتبقى روابط OAuth والجلسات مركزية على `app.example.com`.

## Google والبريد الإلكتروني

احفظ مفاتيح Google كمتغيرات بيئة أو أسرار خدمة، ولا تضعها في GitHub:

```text
Authentication__Google__ClientId=<google-client-id>
Authentication__Google__ClientSecret=<google-client-secret>
```

أضف روابط العودة التالية في إعدادات المزود، مع استبدال الدومين:

```text
https://tasareehapp.com/signin-google
```

في Google استخدم عميلًا من نوع **Web application** واجعل الجمهور **External**.
يدخل مستخدم Google مباشرة، وينشئ النظام له مساحة تجربة كاملة لمدة يومين عند
أول دخول. أما التسجيل المعتاد بالبريد الإلكتروني فيتطلب تأكيد البريد أولاً.

إعدادات SMTP المستخدمة مع Resend:

```text
Email__Smtp__Host=smtp.resend.com
Email__Smtp__Port=587
Email__Smtp__Username=resend
Email__Smtp__Password=<resend-api-key>
Email__Smtp__FromAddress=no-reply@tasareehapp.com
Email__Smtp__FromName=منصة التصاريح
Email__Smtp__EnableSsl=true
```

بعد إنشاء الأسرار أعد تشغيل حاوية التطبيق:

```bash
docker compose -f compose.production.yml --env-file /opt/tasareehapp/.env up -d --build app
```

## ملاحظة التوقيت

النظام الحالي يعتمد توقيتًا محليًا في التصاريح والزيارات، لذلك فُعّل توافق Npgsql الانتقالي. قبل الإطلاق العام يجب توحيد حقول التخزين إلى UTC، وتحويلها إلى توقيت الجهة عند العرض فقط، ثم إزالة وضع التوافق.
