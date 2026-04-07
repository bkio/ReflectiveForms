import { useState, useCallback } from 'react';
import { useFormContext, Controller } from 'react-hook-form';
import { Upload, X, Image, AlertCircle } from 'lucide-react';
import { FieldComponentProps } from './types';

export function MediaField({ schema, path }: FieldComponentProps) {
  const { control } = useFormContext();
  const [preview, setPreview] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  const maxSizeMb = schema.media_options?.max_file_size_mb ?? 8;
  const acceptedTypes = schema.media_options?.accepted_types ?? ['image/*'];
  const previewEnabled = schema.media_options?.preview_enabled ?? true;

  const validateFile = useCallback((file: File): string | null => {
    // Check file size
    if (file.size > maxSizeMb * 1024 * 1024) {
      return `File size must be less than ${maxSizeMb}MB. Current size: ${(file.size / 1024 / 1024).toFixed(2)}MB`;
    }

    // Check file type
    const isValidType = acceptedTypes.some(type => {
      if (type.endsWith('/*')) {
        const category = type.replace('/*', '');
        return file.type.startsWith(category);
      }
      return file.type === type;
    });

    if (!isValidType) {
      return `Invalid file type. Accepted types: ${acceptedTypes.join(', ')}`;
    }

    return null;
  }, [maxSizeMb, acceptedTypes]);

  const handleFile = useCallback((file: File, onChange: (value: string) => void) => {
    setUploadError(null);

    const error = validateFile(file);
    if (error) {
      setUploadError(error);
      return;
    }

    const reader = new FileReader();
    reader.onload = (e) => {
      const base64 = e.target?.result as string;
      setPreview(base64);
      onChange(base64);
    };
    reader.onerror = () => {
      setUploadError('Failed to read file');
    };
    reader.readAsDataURL(file);
  }, [validateFile]);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);
  }, []);

  const handleDrop = useCallback((e: React.DragEvent, onChange: (value: string) => void) => {
    e.preventDefault();
    e.stopPropagation();
    setIsDragging(false);

    const file = e.dataTransfer.files[0];
    if (file) {
      handleFile(file, onChange);
    }
  }, [handleFile]);

  const handleInputChange = useCallback((e: React.ChangeEvent<HTMLInputElement>, onChange: (value: string) => void) => {
    const file = e.target.files?.[0];
    if (file) {
      handleFile(file, onChange);
    }
  }, [handleFile]);

  const handleClear = useCallback((onChange: (value: string) => void) => {
    setPreview(null);
    setUploadError(null);
    onChange('');
  }, []);

  return (
    <Controller
      name={path}
      control={control}
      render={({ field: { value, onChange }, fieldState: { error } }) => (
        <div className="space-y-2">
          {/* Hidden file input — always present so Replace works */}
          <input
            id={`file-input-${path}`}
            type="file"
            accept={acceptedTypes.join(',')}
            className="hidden"
            onChange={(e) => handleInputChange(e, onChange)}
          />

          {/* Preview area */}
          {(preview || value) && previewEnabled ? (
            <div className="relative inline-block group max-w-full">
              <img
                src={preview || value}
                alt="Preview"
                className="w-full max-w-xs max-h-48 rounded-lg border border-gray-200 object-contain"
              />
              <div className="absolute inset-0 bg-black bg-opacity-0 group-hover:bg-opacity-30 transition-all rounded-lg flex items-center justify-center">
                <button
                  type="button"
                  onClick={() => handleClear(onChange)}
                  className="opacity-0 group-hover:opacity-100 p-2 bg-red-500 text-white rounded-full transition-opacity"
                  title="Remove image"
                >
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>
          ) : (
            /* Upload area */
            <div
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={(e) => handleDrop(e, onChange)}
              onClick={() => document.getElementById(`file-input-${path}`)?.click()}
              className={`
                border-2 border-dashed rounded-lg p-4 sm:p-8 text-center cursor-pointer transition-all
                ${isDragging
                  ? 'border-blue-500 bg-blue-50'
                  : error
                    ? 'border-red-300 bg-red-50'
                    : 'border-gray-300 hover:border-gray-400 hover:bg-gray-50'
                }
              `}
            >
              <div className="flex flex-col items-center">
                {isDragging ? (
                  <>
                    <Image className="w-12 h-12 text-blue-500 mb-3" />
                    <p className="text-blue-600 font-medium">Drop your file here</p>
                  </>
                ) : (
                  <>
                    <Upload className="w-12 h-12 text-gray-400 mb-3" />
                    <p className="text-gray-600 font-medium">
                      Drop an image here or click to upload
                    </p>
                    <p className="text-sm text-gray-400 mt-1">
                      Max size: {maxSizeMb}MB
                    </p>
                    {acceptedTypes.length > 0 && (
                      <p className="text-xs text-gray-400 mt-1">
                        Accepted: {acceptedTypes.join(', ')}
                      </p>
                    )}
                  </>
                )}
              </div>
            </div>
          )}

          {/* Error messages */}
          {uploadError && (
            <div className="flex items-center gap-2 text-red-600 text-sm">
              <AlertCircle className="w-4 h-4" />
              <span>{uploadError}</span>
            </div>
          )}

          {error && (
            <p className="text-sm text-red-600">{error.message}</p>
          )}

          {/* Replace button when preview exists */}
          {(preview || value) && (
            <button
              type="button"
              onClick={() => document.getElementById(`file-input-${path}`)?.click()}
              className="text-sm text-blue-600 hover:text-blue-800"
            >
              Replace image
            </button>
          )}
        </div>
      )}
    />
  );
}
