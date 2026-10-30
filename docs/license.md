---
layout: single
permalink: /license/
---

<pre 1>{% include_relative readme/LICENSE.txt %}</pre>
<pre 2><code>{% include_relative readme/LICENSE.txt %}</code></pre>
{% capture license_text %}{% include_relative readme/LICENSE.txt %}{% endcapture %}
<pre 3 style="font-size: small;"><code>{{ license_text | xml_escape }}</code></pre>
{% capture license_text %}{% include_relative readme/LICENSE.txt %}{% endcapture %}
<pre 4 style="font-size: small;"><code>{{ license_text | xml_escape }}</code></pre>