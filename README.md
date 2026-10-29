# TwinMemory — الذاكرة المشتركة
- Android: افتح المجلد في Android Studio (JDK 17) أو ابنِ بـ Gradle 8.9. Application ID: `com.belalmomani.twinmemory`.
- التوقيع: ضع `keystore.properties` محليًا (storeFile, storePassword, keyAlias, keyPassword) أو متغيرات البيئة TM_KEYSTORE_PATH / TM_KEYSTORE_PASSWORD / TM_KEY_ALIAS / TM_KEY_PASSWORD. لا يُرفع إلى Git.
- `app/google-services.json` مستثنى من Git؛ ضعه محليًا أو عبر سر CI.
- `design/`: مصادر الأيقونة (SVG/PNG/ICO) وملف الألوان. ملف .ico جاهز لتطبيق Windows لاحقًا.
