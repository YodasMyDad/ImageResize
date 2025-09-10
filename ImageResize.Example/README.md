# ImageResize Sample Application

This sample demonstrates the ImageResize middleware for ASP.NET Core.

## Setup

1. Place sample images in `wwwroot/images/`
2. Run the application
3. Visit endpoints:

### Middleware Usage
```
GET /media/sample.jpg?width=800&height=600&quality=80
```

### Programmatic Usage
```
GET /demo
```

### Info
```
GET /info
```

## Features Demonstrated

- Querystring-based resizing (`?width=`, `?height=`, `?quality=`)
- Aspect ratio preservation (fit within bounds)
- Disk caching with atomic writes
- HTTP caching headers (ETag, Last-Modified, Cache-Control)
- Configurable bounds and quality settings
- Multiple image formats (JPEG, PNG, WebP, GIF, BMP, TIFF)

## Configuration

See `appsettings.json` for all available configuration options.
