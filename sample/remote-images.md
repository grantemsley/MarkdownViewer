# Remote image test (CSP `img-src`)

This file checks that the new Content Security Policy still allows images
over `https:` and `data:`. **All three images below should render.** If any
shows a broken-image icon, the CSP's `img-src` is too tight — report which one.

## 1. Remote SVG (shields.io badge, served over HTTPS)

![CSP badge](https://img.shields.io/badge/remote%20image-loaded-brightgreen)
![HTTPS badge](https://img.shields.io/badge/img--src-https%3A-blue)

## 2. Remote raster image (HTTPS)

![Seeded sample photo](https://picsum.photos/seed/markdownviewer/320/160)

## 3. Inline `data:` URI image

![Inline data URI](data:image/svg+xml,%3Csvg%20xmlns='http://www.w3.org/2000/svg'%20width='220'%20height='80'%3E%3Crect%20width='220'%20height='80'%20rx='8'%20fill='%234c8bf5'/%3E%3Ctext%20x='110'%20y='48'%20font-size='20'%20fill='white'%20text-anchor='middle'%3Edata%3A%20image%20OK%3C/text%3E%3C/svg%3E)

---

**Expected:** two green/blue badges, one photo, and a blue rounded box reading
"data: image OK". Requires network access for #1 and #2.
