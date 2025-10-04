# ImageResize Sample Application

This sample demonstrates the ImageResize middleware for ASP.NET Core.

## Setup

1. Place sample images in `wwwroot/images/`
2. Run the application
3. Visit endpoints:

### Middleware Usage
```
GET /images/sample.jpg?width=800&height=600&quality=80
GET /img/sample.jpg?width=800&height=600&quality=80
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
- **Image upload demo**: Upload your own images to test resize functionality in real-time

## Image Upload Feature

The home page includes an upload form that allows you to test the ImageResize middleware with your own images:

1. Click "Choose File" and select an image (up to 10MB)
2. Click "Upload & Resize"
3. Your image will be saved to `wwwroot/uploads/` and displayed in three sizes (thumbnail, medium, large)
4. Click "Reset to Default" to return to the sample image

Uploaded images are automatically cleaned up and excluded from git via `.gitignore`.

## Configuration

See `appsettings.json` for all available configuration options.
