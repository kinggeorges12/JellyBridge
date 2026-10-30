---
layout: single
permalink: /license/
---

**XML Escape:**
{% capture license_text1 %}{% include_relative readme/LICENSE.txt %}{% endcapture %}
<pre style="font-size: small;"><code>{{ license_text1 | xml_escape }}</code></pre>

**HTML Escape:**
{% capture license_text2 %}{% include_relative readme/LICENSE.txt %}{% endcapture %}
<pre style="font-size: small;"><code>{{ license_text2 | escape }}</code></pre>